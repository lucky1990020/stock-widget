using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;

namespace StockWidgetNative;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("7A6F3D2C-5E4B-4A91-8C7D-3F2E1B0A9C84")]
public sealed class WidgetProvider : IWidgetProvider
{
    private const string DefinitionId = "Stock_Widget";
    private const int RefreshSeconds = 30;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, WidgetInstance> Instances = new(StringComparer.Ordinal);
    private static readonly ManualResetEvent EmptyWidgetListEvent = new(false);
    private static readonly QuoteService Quotes = new();

    private static Timer? _timer;
    private static bool _recovered;
    private static int _refreshing;
    private static DateTime _lastAttempt = DateTime.MinValue;

    public WidgetProvider()
    {
        Log.Write("WidgetProvider constructed");
        RecoverRunningWidgets();
        EnsureTimer();
    }

    public static ManualResetEvent GetEmptyWidgetListEvent()
    {
        return EmptyWidgetListEvent;
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        string id = widgetContext.Id;
        Log.Write($"CreateWidget {id} {widgetContext.DefinitionId}");

        lock (Gate)
        {
            Instances[id] = new WidgetInstance { Active = true };
        }

        PushCard(id);
        TriggerRefresh();
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        Log.Write($"DeleteWidget {widgetId}");

        bool empty;
        lock (Gate)
        {
            Instances.Remove(widgetId);
            empty = Instances.Count == 0;
        }

        if (empty)
        {
            EmptyWidgetListEvent.Set();
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        string id = widgetContext.Id;

        lock (Gate)
        {
            if (!Instances.TryGetValue(id, out var instance))
            {
                instance = new WidgetInstance();
                Instances[id] = instance;
            }

            instance.Active = true;
        }

        PushCard(id);

        if (DateTime.UtcNow - _lastAttempt > TimeSpan.FromSeconds(10))
        {
            TriggerRefresh();
        }
    }

    public void Deactivate(string widgetId)
    {
        lock (Gate)
        {
            if (Instances.TryGetValue(widgetId, out var instance))
            {
                instance.Active = false;
            }
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        // The card uses $host.widgetSize, so resend it to make the host
        // re-evaluate the medium/large conditional elements.
        PushCard(contextChangedArgs.WidgetContext.Id);
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        // The current card has no interactive actions.
    }

    private static void RecoverRunningWidgets()
    {
        lock (Gate)
        {
            if (_recovered)
            {
                return;
            }

            _recovered = true;

            try
            {
                var infos = WidgetManager.GetDefault().GetWidgetInfos();
                if (infos is null)
                {
                    return;
                }

                foreach (var info in infos)
                {
                    var context = info.WidgetContext;
                    if (context.DefinitionId == DefinitionId &&
                        !Instances.ContainsKey(context.Id))
                    {
                        Instances[context.Id] = new WidgetInstance();
                        Log.Write($"Recovered widget {context.Id}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("RecoverRunningWidgets failed", ex);
            }
        }
    }

    private static void EnsureTimer()
    {
        lock (Gate)
        {
            _timer ??= new Timer(
                _ => OnTimerTick(),
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(RefreshSeconds));
        }
    }

    private static void OnTimerTick()
    {
        if (!HasActiveWidgets())
        {
            return;
        }

        TriggerRefresh();
    }

    private static bool HasActiveWidgets()
    {
        lock (Gate)
        {
            return Instances.Values.Any(w => w.Active);
        }
    }

    private static void TriggerRefresh()
    {
        _ = RefreshAndPushAsync();
    }

    private static async Task RefreshAndPushAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            await Quotes.RefreshAsync().ConfigureAwait(false);
            _lastAttempt = DateTime.UtcNow;

            string card;
            try
            {
                card = Quotes.BuildCard();
            }
            catch (Exception ex)
            {
                Log.Error("BuildCard failed", ex);
                return;
            }

            string[] activeIds;
            lock (Gate)
            {
                activeIds = Instances
                    .Where(pair => pair.Value.Active)
                    .Select(pair => pair.Key)
                    .ToArray();
            }

            foreach (string id in activeIds)
            {
                PushCard(id, card);
            }
        }
        catch (Exception ex)
        {
            _lastAttempt = DateTime.UtcNow;
            Log.Error("RefreshAndPushAsync failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private static void PushCard(string widgetId, string? card = null)
    {
        try
        {
            card ??= Quotes.BuildCard();

            var options = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = card,
                Data = "{}",
                CustomState = string.Empty,
                IsPlaceholderContent = false
            };

            WidgetManager.GetDefault().UpdateWidget(options);
        }
        catch (Exception ex)
        {
            Log.Error($"UpdateWidget {widgetId} failed", ex);
        }
    }

    private sealed class WidgetInstance
    {
        public bool Active { get; set; }
    }
}

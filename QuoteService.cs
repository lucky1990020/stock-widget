using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace StockWidgetNative;

internal sealed class QuoteService
{
    private const int SparklineWidth = 10;

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly string[] DefaultCodes = { "600519", "300750", "002594" };

    private readonly object _gate = new();
    private readonly Dictionary<string, Quote> _quotes = new(StringComparer.Ordinal);
    private List<StockDef> _defs;
    private DateTime _lastUpdate = DateTime.MinValue;
    private string? _error;

    internal sealed record StockDef(string Label, string? SecId, bool IsIndex);

    internal sealed record Quote(
        string Name,
        double Price,
        double Pct,
        double Change,
        string Sparkline = "");

    internal sealed record Snapshot(
        IReadOnlyList<StockDef> Defs,
        IReadOnlyDictionary<string, Quote> Quotes,
        DateTime UpdateTime,
        string? Error,
        string MarketText);

    public QuoteService()
    {
        _defs = BuildDefinitions();
    }

    public Snapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new Snapshot(
                _defs.ToArray(),
                new Dictionary<string, Quote>(_quotes, StringComparer.Ordinal),
                _lastUpdate,
                _error,
                GetMarketStatus());
        }
    }

    public string BuildCard()
    {
        return StockCardBuilder.Build(GetSnapshot());
    }

    public async Task RefreshAsync()
    {
        var defs = BuildDefinitions();
        var fetched = await FetchQuotesAsync(defs).ConfigureAwait(false);

        if (fetched.Count > 0)
        {
            var sparklines = await FetchSparklinesAsync(defs).ConfigureAwait(false);
            foreach (var pair in sparklines)
            {
                if (fetched.TryGetValue(pair.Key, out var quote))
                {
                    fetched[pair.Key] = quote with { Sparkline = pair.Value };
                }
            }
        }

        lock (_gate)
        {
            _defs = defs;

            if (fetched.Count > 0)
            {
                foreach (var pair in fetched)
                {
                    _quotes[pair.Key] = pair.Value;
                }

                _lastUpdate = BeijingNow();
                _error = null;
            }
            else
            {
                _error = "网络异常";
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri("https://quote.eastmoney.com/");
        return client;
    }

    private static async Task<Dictionary<string, Quote>> FetchQuotesAsync(List<StockDef> defs)
    {
        var result = new Dictionary<string, Quote>(StringComparer.Ordinal);
        var secids = string.Join(",", defs
            .Where(d => !string.IsNullOrEmpty(d.SecId))
            .Select(d => d.SecId));

        if (string.IsNullOrEmpty(secids))
        {
            return result;
        }

        try
        {
            string url =
                "https://push2.eastmoney.com/api/qt/ulist.np/get" +
                "?ut=bd1d9ddb04089700cf9c27f6f7426281&fltt=2&invt=2" +
                "&fields=f2,f3,f4,f12,f13,f14&secids=" +
                Uri.EscapeDataString(secids) +
                "&_=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string text = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("diff", out var diff) ||
                diff.ValueKind != JsonValueKind.Array)
            {
                Log.Error("Eastmoney quote response missing data.diff");
                return result;
            }

            foreach (var item in diff.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string code = GetScalarString(item, "f12");
                string market = GetScalarString(item, "f13");
                double? price = GetDouble(item, "f2");

                if (string.IsNullOrEmpty(code) ||
                    string.IsNullOrEmpty(market) ||
                    price is null)
                {
                    continue;
                }

                string secid = market + "." + code;
                result[secid] = new Quote(
                    GetString(item, "f14"),
                    price.Value,
                    GetDouble(item, "f3") ?? 0,
                    GetDouble(item, "f4") ?? 0);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Eastmoney quote request failed", ex);
        }

        return result;
    }

    private static async Task<Dictionary<string, string>> FetchSparklinesAsync(List<StockDef> defs)
    {
        var tasks = defs
            .Where(d => !string.IsNullOrEmpty(d.SecId))
            .Select(d => FetchOneSparklineAsync(d.SecId!))
            .ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (secid, sparkline) in results)
        {
            if (!string.IsNullOrEmpty(sparkline))
            {
                map[secid] = sparkline;
            }
        }

        return map;
    }

    private static async Task<(string SecId, string Sparkline)> FetchOneSparklineAsync(string secid)
    {
        try
        {
            string url =
                "https://push2his.eastmoney.com/api/qt/stock/trends2/get" +
                "?ut=bd1d9ddb04089700cf9c27f6f7426281" +
                "&secid=" + Uri.EscapeDataString(secid) +
                "&fields1=f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13" +
                "&fields2=f51,f52,f53,f54,f55,f56,f57,f58" +
                "&iscr=0&ndays=1&_=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string text = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("trends", out var trends) ||
                trends.ValueKind != JsonValueKind.Array)
            {
                return (secid, string.Empty);
            }

            var values = new List<double>();
            foreach (var item in trends.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string[] parts = (item.GetString() ?? string.Empty).Split(',');
                if (parts.Length >= 3 &&
                    double.TryParse(
                        parts[2],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double close) &&
                    close > 0)
                {
                    values.Add(close);
                }
            }

            return (secid, BuildSparkline(values));
        }
        catch
        {
            return (secid, string.Empty);
        }
    }

    private static string BuildSparkline(List<double> values)
    {
        if (values.Count < 2)
        {
            return string.Empty;
        }

        double min = values.Min();
        double max = values.Max();

        if (max - min < 0.000001)
        {
            return new string((char)0x2584, SparklineWidth);
        }

        var builder = new StringBuilder(SparklineWidth);
        for (int i = 0; i < SparklineWidth; i++)
        {
            double ratio = i / (double)(SparklineWidth - 1);
            int index = (int)Math.Round(ratio * (values.Count - 1));
            int level = (int)Math.Round((values[index] - min) / (max - min) * 7);
            level = Math.Clamp(level, 0, 7);
            builder.Append((char)(0x2581 + level));
        }

        return builder.ToString();
    }

    private static List<StockDef> BuildDefinitions()
    {
        var defs = new List<StockDef>
        {
            new("上证指数", "1.000001", true),
            new("深证成指", "0.399001", true),
            new("创业板指", "0.399006", true),
            new("科创50", "1.000688", true)
        };

        var custom = LoadCustomCodes();
        for (int i = 0; i < 3; i++)
        {
            defs.Add(ParseCustomDef(custom[i]));
        }

        return defs;
    }

    private static StockDef ParseCustomDef(string raw)
    {
        string text = (raw ?? string.Empty).Trim().Replace('，', ',');
        if (string.IsNullOrWhiteSpace(text))
        {
            return new StockDef("未设置", null, false);
        }

        string codeText = text;
        string hint = string.Empty;
        int comma = text.IndexOf(',');
        if (comma >= 0)
        {
            codeText = text[..comma].Trim();
            hint = text[(comma + 1)..].Trim();
        }
        else
        {
            int space = text.IndexOfAny(new[] { ' ', '\t' });
            if (space > 0)
            {
                codeText = text[..space].Trim();
                hint = text[(space + 1)..].Trim();
            }
        }

        string lower = codeText.ToLowerInvariant();
        string? prefix = null;
        string? code = null;

        if (lower.Length == 8 &&
            (lower.StartsWith("sh", StringComparison.Ordinal) ||
             lower.StartsWith("sz", StringComparison.Ordinal) ||
             lower.StartsWith("bj", StringComparison.Ordinal)) &&
            lower[2..].All(char.IsDigit))
        {
            prefix = lower[..2];
            code = lower[2..];
        }
        else if (codeText.Length == 6 && codeText.All(char.IsDigit))
        {
            code = codeText;
            if (code.StartsWith('4') ||
                code.StartsWith('8') ||
                code.StartsWith("92", StringComparison.Ordinal))
            {
                prefix = "bj";
            }
            else if (code.StartsWith('6') ||
                     code.StartsWith('9') ||
                     code.StartsWith('5'))
            {
                prefix = "sh";
            }
            else
            {
                prefix = "sz";
            }
        }

        if (prefix is null || code is null)
        {
            return new StockDef(text, null, false);
        }

        string market = prefix == "sh" ? "1" : "0";
        string label = string.IsNullOrEmpty(hint) ? code : hint;
        return new StockDef(label, market + "." + code, false);
    }

    private static string[] LoadCustomCodes()
    {
        var values = new[] { string.Empty, string.Empty, string.Empty };

        try
        {
            Directory.CreateDirectory(DataDirectory);
            if (!File.Exists(ConfigPath))
            {
                var initial = new AppConfig { customStocks = DefaultCodes.ToArray() };
                File.WriteAllText(
                    ConfigPath,
                    JsonSerializer.Serialize(initial, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
            }

            var config = JsonSerializer.Deserialize<AppConfig>(
                File.ReadAllText(ConfigPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config?.customStocks is not null)
            {
                for (int i = 0; i < 3 && i < config.customStocks.Length; i++)
                {
                    values[i] = config.customStocks[i] ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Load config failed", ex);
        }

        return values;
    }

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StockWidgetNative");

    private static string ConfigPath => Path.Combine(DataDirectory, "config.json");

    private static DateTime BeijingNow()
    {
        return DateTime.UtcNow.AddHours(8);
    }

    private static string GetMarketStatus()
    {
        var now = BeijingNow();
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return "周末休市";
        }

        int minutes = now.Hour * 60 + now.Minute;
        if ((minutes >= 555 && minutes <= 690) || (minutes >= 780 && minutes <= 900))
        {
            return "交易中";
        }

        if (minutes < 555)
        {
            return "未开盘";
        }

        if (minutes > 690 && minutes < 780)
        {
            return "午间休市";
        }

        return "已收盘";
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string GetScalarString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        return null;
    }

    private sealed class AppConfig
    {
        public string[]? customStocks { get; set; }
    }
}

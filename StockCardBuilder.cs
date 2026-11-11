using System.Globalization;
using System.Text.Json;

namespace StockWidgetNative;

internal static class StockCardBuilder
{
    private const string LargeOnly = "${$host.widgetSize==\"large\"}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string Build(QuoteService.Snapshot snapshot)
    {
        var body = new List<object?>();
        body.Add(BuildHeader(snapshot));

        foreach (var def in snapshot.Defs.Where(d => d.IsIndex))
        {
            body.Add(BuildRow(def, snapshot, null));
        }

        body.Add(BuildSection("我的自选", LargeOnly));

        foreach (var def in snapshot.Defs.Where(d => !d.IsIndex))
        {
            body.Add(BuildRow(def, snapshot, LargeOnly));
        }

        body.Add(BuildFooter(snapshot));

        var card = new Dictionary<string, object?>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body
        };

        return JsonSerializer.Serialize(card, JsonOptions);
    }

    private static Dictionary<string, object?> BuildHeader(QuoteService.Snapshot snapshot)
    {
        string status = snapshot.UpdateTime == DateTime.MinValue
            ? "正在获取行情"
            : snapshot.MarketText + " · " + snapshot.UpdateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            status = "网络异常 · " + status;
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "none",
            ["columns"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "stretch",
                    ["verticalContentAlignment"] = "center",
                    ["items"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "TextBlock",
                            ["text"] = "A股行情",
                            ["size"] = "medium",
                            ["weight"] = "bolder"
                        }
                    }
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "auto",
                    ["verticalContentAlignment"] = "center",
                    ["items"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "TextBlock",
                            ["text"] = status,
                            ["size"] = "small",
                            ["isSubtle"] = true,
                            ["horizontalAlignment"] = "right",
                            ["wrap"] = false
                        }
                    }
                }
            }
        };
    }

    private static Dictionary<string, object?> BuildSection(string text, string? when)
    {
        var section = new Dictionary<string, object?>
        {
            ["type"] = "TextBlock",
            ["text"] = text,
            ["size"] = "small",
            ["weight"] = "bolder",
            ["isSubtle"] = true,
            ["spacing"] = "medium"
        };

        if (when is not null)
        {
            section["$when"] = when;
        }

        return section;
    }

    private static Dictionary<string, object?> BuildRow(
        QuoteService.StockDef def,
        QuoteService.Snapshot snapshot,
        string? when)
    {
        QuoteService.Quote? quote = null;
        if (!string.IsNullOrEmpty(def.SecId))
        {
            snapshot.Quotes.TryGetValue(def.SecId, out quote);
        }

        string name = quote is not null && !string.IsNullOrWhiteSpace(quote.Name)
            ? quote.Name
            : def.Label;

        string price = quote is not null ? FormatPrice(quote.Price) : "--";
        string pct = quote is not null ? FormatSigned(quote.Pct) + "%" : "--";

        string? color = null;
        if (quote is not null)
        {
            if (quote.Change > 0)
            {
                color = "attention";
            }
            else if (quote.Change < 0)
            {
                color = "good";
            }
        }

        var priceText = new Dictionary<string, object?>
        {
            ["type"] = "TextBlock",
            ["text"] = price,
            ["size"] = "medium",
            ["weight"] = "bolder",
            ["horizontalAlignment"] = "right",
            ["wrap"] = false
        };

        if (color is not null)
        {
            priceText["color"] = color;
        }

        var pctText = new Dictionary<string, object?>
        {
            ["type"] = "TextBlock",
            ["text"] = pct,
            ["size"] = "small",
            ["weight"] = "bolder",
            ["horizontalAlignment"] = "right",
            ["spacing"] = "none",
            ["wrap"] = false
        };

        if (color is not null)
        {
            pctText["color"] = color;
        }

        var sparkText = new Dictionary<string, object?>
        {
            ["type"] = "TextBlock",
            ["text"] = quote?.Sparkline ?? string.Empty,
            ["size"] = "small",
            ["fontType"] = "monospace",
            ["horizontalAlignment"] = "center",
            ["verticalAlignment"] = "center",
            ["spacing"] = "none",
            ["wrap"] = false
        };

        if (color is not null)
        {
            sparkText["color"] = color;
        }

        var row = new Dictionary<string, object?>
        {
            ["type"] = "ColumnSet",
            ["separator"] = true,
            ["spacing"] = "none",
            ["columns"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "auto",
                    ["verticalContentAlignment"] = "center",
                    ["items"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "TextBlock",
                            ["text"] = name,
                            ["size"] = "small",
                            ["weight"] = "bolder",
                            ["wrap"] = false
                        }
                    }
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "stretch",
                    ["verticalContentAlignment"] = "center",
                    ["items"] = new object?[] { sparkText }
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "auto",
                    ["verticalContentAlignment"] = "center",
                    ["items"] = new object?[] { priceText, pctText }
                }
            }
        };

        if (when is not null)
        {
            row["$when"] = when;
        }

        return row;
    }

    private static Dictionary<string, object?> BuildFooter(QuoteService.Snapshot snapshot)
    {
        string text = snapshot.UpdateTime == DateTime.MinValue
            ? "数据正在加载"
            : "更新于 " + snapshot.UpdateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
              " · 仅供参考";

        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            text = "网络异常，显示上次行情 · 仅供参考";
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "TextBlock",
            ["text"] = text,
            ["size"] = "small",
            ["isSubtle"] = true,
            ["spacing"] = "medium",
            ["wrap"] = false
        };
    }

    private static string FormatPrice(double value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string FormatSigned(double value)
    {
        return value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
    }
}

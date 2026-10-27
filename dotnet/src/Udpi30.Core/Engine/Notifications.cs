using System.Text;
using System.Text.Json.Nodes;

namespace Udpi30.Core.Engine;

/// <summary>Sends deployment alerts to Slack / Microsoft Teams. Mirrors <c>engine/notifications.py</c>.</summary>
public static class Notifications
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static bool SendSlack(string webhookUrl, string message)
    {
        try
        {
            var body = new JsonObject { ["text"] = message }.ToJsonString();
            using var resp = Http.PostAsync(webhookUrl, new StringContent(body, Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();
            return (int)resp.StatusCode == 200;
        }
        catch
        {
            return false;
        }
    }

    public static bool SendTeams(string webhookUrl, string message)
    {
        try
        {
            var card = new JsonObject
            {
                ["@type"] = "MessageCard",
                ["summary"] = "Fabric Deployment Deployment",
                ["text"] = message,
            }.ToJsonString();
            using var resp = Http.PostAsync(webhookUrl, new StringContent(card, Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();
            return (int)resp.StatusCode == 200;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Send a notification based on a single notification config + context substitutions.</summary>
    public static void Notify(Models.NotificationConfig config, IReadOnlyDictionary<string, string> context)
    {
        var message = config.Message;
        foreach (var (key, value) in context)
        {
            message = message.Replace($"{{{key}}}", value);
        }

        if (string.IsNullOrEmpty(config.Webhook))
        {
            return;
        }

        switch (config.Type)
        {
            case "slack":
                SendSlack(config.Webhook, message);
                break;
            case "teams":
                SendTeams(config.Webhook, message);
                break;
        }
    }
}

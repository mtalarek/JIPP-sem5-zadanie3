using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Zadanie3.Helpers;

public static class ChatMessageExtensions
{
    public static string Serialize(this ChatMessage message)
    {
        var sb = new StringBuilder();
        foreach (var content in message.Contents)
            if (content is TextContent text)
                sb.Append(text.Text);

        return JsonSerializer.Serialize(new
        {
            role = message.Role.ToString(),
            content = sb.ToString()
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
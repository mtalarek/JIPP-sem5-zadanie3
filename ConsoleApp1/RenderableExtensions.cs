using Microsoft.Extensions.AI;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;

namespace Zadanie3.Helpers;

public static class RenderableExtensions
{
    public static IRenderable AsRenderable(this IEnumerable<ChatMessage> messages)
        => new Rows(messages.Select(m => m.AsRenderable()));

    public static IRenderable AsRenderable(this ChatMessage message)
    {
        var sb = new StringBuilder();
        foreach (var content in message.Contents)
            if (content is TextContent text)
                sb.Append(text.Text);

        return new Panel(sb.ToString())
            .Header(message.Role.ToString(), Justify.Left);
    }

    public static IRenderable AsRenderable(this StringBuilder sb)
        => new Markup(sb.ToString());
}
using System;
using Scriban;
using Scriban.Runtime;

namespace Counterpoint.Devices.Printing.Templates;

/// <summary>
/// Renders a Scriban template body against a <see cref="ReceiptTemplateModel"/> - the whole of
/// what makes the receipt layout owner-editable without a rebuild (SRS FR-7.3, NFR-M1).
/// </summary>
/// <remarks>
/// A thin wrapper, deliberately: everything about what a rendered line <em>means</em> is
/// <see cref="ReceiptDirectiveParser"/>'s job, not this class's. This only turns template source
/// text plus a model into the text the parser reads.
/// </remarks>
public static class ScribanReceiptTemplateEngine
{
    /// <summary>
    /// Parses and renders <paramref name="templateText"/> against <paramref name="model"/>.
    /// </summary>
    /// <exception cref="ReceiptTemplateException">
    /// The template has a syntax error, or threw while rendering (for example a template that
    /// references a field the model does not have). The caller decides what "a broken template"
    /// means for a sale in progress - this never silently produces half a receipt.
    /// </exception>
    public static string Render(string templateText, ReceiptTemplateModel model)
    {
        ArgumentException.ThrowIfNullOrEmpty(templateText);
        ArgumentNullException.ThrowIfNull(model);

        var template = Template.Parse(templateText);

        if (template.HasErrors)
        {
            throw new ReceiptTemplateException(
                "The receipt template has a syntax error: " + string.Join("; ", template.Messages));
        }

        var scriptObject = new ScriptObject();
        scriptObject.Import(model, renamer: StandardMemberRenamer.Rename);

        var context = new TemplateContext { MemberRenamer = StandardMemberRenamer.Rename };
        context.PushGlobal(scriptObject);

        try
        {
            return template.Render(context);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            throw new ReceiptTemplateException("The receipt template could not be rendered: " + ex.Message, ex);
        }
    }
}

using System.Collections.Concurrent;
using Quro.Grid.Data;
using QuroErp.Contracts.Common.Codes;

namespace QuroErp.Web.Common;

/// <summary>
/// Code lists as data sets for combo boxes and grid combo columns, read once per code Major and language and shared by
/// the screens of a circuit (qurogrid-integration.md 10 "공통코드 콤보"). The stored value is the minor_code.
/// </summary>
public sealed class CodeDataSets(CodeApiClient codes)
{
    private readonly ConcurrentDictionary<(string Major, string Lang), Task<QuroDataSet<CodeItemDto>>> _sets = new();

    /// <summary>The codes of a Major in a language. A failed read is not kept, so the next screen reads again.</summary>
    public Task<QuroDataSet<CodeItemDto>> GetAsync(string major, string lang) => _sets.GetOrAdd((major, lang), _ => ReadAsync(major, lang));

    private async Task<QuroDataSet<CodeItemDto>> ReadAsync(string major, string lang)
    {
        try
        {
            var set = new QuroDataSet<CodeItemDto>();
            set.Load(await codes.GetAsync(major, lang, CancellationToken.None));
            return set;
        }
        catch
        {
            _sets.TryRemove((major, lang), out _);
            throw;
        }
    }
}

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>Item image upload screen record (BC-UC-010): the item and the file picked but not uploaded yet.</summary>
public sealed class ItemImageModel
{
    public long Id { get; set; }

    public string? ItemCd { get; set; }

    public string? ItemName { get; set; }

    /// <summary>The picked file; set until it is uploaded, so leaving the screen asks first.</summary>
    public string? FileName { get; set; }

    public long FileSize { get; set; }
}

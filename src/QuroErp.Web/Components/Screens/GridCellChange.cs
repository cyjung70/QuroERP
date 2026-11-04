namespace QuroErp.Web.Components.Screens;

/// <summary>A cell a user changed on a <c>QuroMultiGrid</c>: the data set row key, the column field and the value now in the cell.</summary>
public sealed record GridCellChange(int RowKey, string Field, object? Value);

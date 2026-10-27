namespace QuroErp.Web.Components.Screens;

/// <summary>A document of a Single + Multi (SM) screen: header and lines.</summary>
public sealed record HeaderLines<THeader, TLine>(THeader Header, IReadOnlyList<TLine> Lines);

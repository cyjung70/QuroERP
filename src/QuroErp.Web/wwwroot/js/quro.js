// Browser helpers for the Quro screen components.

// Saves a file sent from .NET (DotNetStreamReference), e.g. the Excel export of a Multi grid.
window.quroDownload = async (fileName, streamReference) => {
    const buffer = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer]));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
};

// Submits a normal (non-interactive) form, e.g. the culture form after the my info language changes.
window.quroSubmit = (form) => form.requestSubmit();

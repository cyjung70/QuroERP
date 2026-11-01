// QuroERP shared browser helpers
window.quro = window.quro || {};

// Direct file download (PDF from FastReport.NET) — no preview tab.
window.quro.downloadFile = function (fileName, contentType, base64) {
  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
  const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
};

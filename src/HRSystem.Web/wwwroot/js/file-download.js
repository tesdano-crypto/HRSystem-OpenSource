window.hrSystemDownloads = window.hrSystemDownloads || {};

window.hrSystemDownloads.download = async function (url) {
    const response = await fetch(url, {
        credentials: "same-origin",
        headers: { "Accept": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
    });
    if (!response.ok) {
        throw new Error("Attendance workbook download failed.");
    }

    const blob = await response.blob();
    const disposition = response.headers.get("Content-Disposition") || "";
    const utf8Name = disposition.match(/filename\*=UTF-8''([^;]+)/i);
    const plainName = disposition.match(/filename="?([^";]+)"?/i);
    const fileName = utf8Name
        ? decodeURIComponent(utf8Name[1])
        : plainName?.[1] || "Attendance.xlsx";
    const objectUrl = URL.createObjectURL(blob);
    try {
        const anchor = document.createElement("a");
        anchor.href = objectUrl;
        anchor.download = fileName;
        anchor.style.display = "none";
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    } finally {
        URL.revokeObjectURL(objectUrl);
    }
};

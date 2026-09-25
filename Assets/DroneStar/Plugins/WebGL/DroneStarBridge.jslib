// Browser side of DroneStar.App.WebBridge: text downloads and a file picker.
mergeInto(LibraryManager.library, {
  DroneStar_DownloadText: function (fileNamePtr, textPtr, mimePtr) {
    var fileName = UTF8ToString(fileNamePtr);
    var text = UTF8ToString(textPtr);
    var mime = UTF8ToString(mimePtr) || "text/plain";
    var blob = new Blob([text], { type: mime + ";charset=utf-8" });
    var url = URL.createObjectURL(blob);
    var link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";
    document.body.appendChild(link);
    link.click();
    setTimeout(function () {
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    }, 2000);
  },

  DroneStar_OpenTextFile: function (objectPtr, methodPtr, acceptPtr) {
    var objectName = UTF8ToString(objectPtr);
    var method = UTF8ToString(methodPtr);
    var accept = UTF8ToString(acceptPtr);
    var input = document.createElement("input");
    input.type = "file";
    input.accept = accept;
    input.style.display = "none";
    var cleanup = function () {
      if (input.parentNode) input.parentNode.removeChild(input);
    };
    input.onchange = function () {
      var file = input.files && input.files[0];
      cleanup();
      if (!file) return;
      if (file.size > 2 * 1024 * 1024) {
        SendMessage(objectName, method, "!too-large");
        return;
      }
      var reader = new FileReader();
      reader.onload = function () { SendMessage(objectName, method, String(reader.result)); };
      reader.onerror = function () { SendMessage(objectName, method, "!read-error"); };
      reader.readAsText(file);
    };
    document.body.appendChild(input);
    input.click();
  }
});

var backendUrl = "https://codepointsplz.azurewebsites.net/api/WebBackend";
var getUrlParameter = function getUrlParameter(sParam) {
    var sPageURL = decodeURIComponent(window.location.search.substring(1)),
        sURLVariables = sPageURL.split('&'),
        sParameterName,
        i;

    for (i = 0; i < sURLVariables.length; i++) {
        sParameterName = sURLVariables[i].split('=');

        if (sParameterName[0] === sParam) {
            return sParameterName[1] === undefined ? true : sParameterName[1];
        }
    }
};

function FormatCodepointCode(codepoint) {
    var hex = Number(codepoint).toString(16).toUpperCase();

    while (hex.length < 4) {
        hex = "0" + hex;
    }

    return "U+" + hex;
}

function EscapeHtml(unsafe) {
    return unsafe
         .replace(/&/g, "&amp;")
         .replace(/</g, "&lt;")
         .replace(/>/g, "&gt;")
         .replace(/"/g, "&quot;")
         .replace(/'/g, "&#039;");
 }

function Render(codepointData) {
    var t = "<table>"
    t += "<tr><th>Codepoint</th><th>Name</th><th>Value</th></tr>";

    $.each(codepointData.Codepoints, function (i, codepoint) {
        t += "<tr>";
        t += "<td>" + FormatCodepointCode(codepoint.Codepoint) + "</td>";
        t += "<td>" + codepoint.Name + "</td>";
        t += "<td>" + EscapeHtml(String.fromCodePoint(codepoint.Codepoint)) + "</td>";
        t += "</tr>";
    });

    t += "</table>";

    var tweetHtml = codepointData.EmbedHtml.replace("<blockquote class=\"twitter-tweet\"", "<blockquote class=\"twitter-tweet tw-align-center\"");
    $("#codepoints").append(tweetHtml);
    $("#codepoints").append(t);
}

function RenderError(errorMessage) {
    var e = "<div class='error'>"
    e += errorMessage;
    e += "</div>";

    $("#codepoints").append(e);
}

function Codepoints() {
    var id = getUrlParameter('tid');
    if (id === undefined) {
        RenderError("Tweet ID not provided");
    }

    var url = backendUrl + "?tid=" + id;
    $.ajax(url, {
        headers: {},
        dataType: "json",
        success: function (data, status, xhr) {
            Render(data);
        },
        error: function () {
            RenderError("Error getting codepoint data");
        }
    });
}
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

function CodepointTable(codepoints) {
    var t = "<table>"
    t += "<tr><th>Codepoint</th><th>Name</th><th>Value</th></tr>";

    $.each(codepoints, function (i, codepoint) {
        t += "<tr>";
        t += "<td>" + FormatCodepointCode(codepoint.Codepoint) + "</td>";
        t += "<td>" + codepoint.Name + "</td>";
        t += "<td>" + EscapeHtml(String.fromCodePoint(codepoint.Codepoint)) + "</td>";
        t += "</tr>";
    });

    t += "</table>";

    return t;
}
function Render(codepointData) {
    var mentionHtml = codepointData.MentionEmbedHtml.replace("<blockquote class=\"twitter-tweet\"", "<blockquote class=\"twitter-tweet tw-align-center\"");
    $("#codepoints").append(mentionHtml);

    if (codepointData.Codepoints && !codepointData.TargetEmbedHtml) {
        // single tweet
        var table = CodepointTable(codepointData.Codepoints);
        $("#codepoints").append(table);
    } else if (codepointData.Codepoints && codepointData.TargetEmbedHtml) {
        // quoted or replied tweet
        $("#codepoints").append("<h3>Referenced tweet</h3>");
        var referenceHtml = codepointData.TargetEmbedHtml.replace("<blockquote class=\"twitter-tweet\"", "<blockquote class=\"twitter-tweet tw-align-center\"");
        $("#codepoints").append(referenceHtml);
        
        var table = CodepointTable(codepointData.Codepoints);
        $("#codepoints").append(table);
    } else if (codepointData.ScreenName) {
        // user profile
        $("#codepoints").append("<h3>Screen Name</h3>");
        $("#codepoints").append("<div><p>" + EscapeHtml(codepointData.ScreenName) + "</p></div>");
        $("#codepoints").append(CodepointTable(codepointData.ScreenNameCodepoints));

        $("#codepoints").append("<h3>Display Name</h3>");
        $("#codepoints").append("<div><p>" + EscapeHtml(codepointData.DisplayName) + "</p></div>");
        $("#codepoints").append(CodepointTable(codepointData.DisplayNameCodepoints));

        $("#codepoints").append("<h3>Description</h3>");
        $("#codepoints").append("<div><p>" + EscapeHtml(codepointData.Summary) + "</p></div>");
        $("#codepoints").append(CodepointTable(codepointData.SummaryCodepoints));
    }
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
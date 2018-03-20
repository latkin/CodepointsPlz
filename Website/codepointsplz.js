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

function Render(codepointData) {
    var t = "<table>"
    t += "<tr><th>Codepoint</th><th>Name</th></tr>";

    $.each(codepointData.Codepoints, function (i, codepoint) {
        t += "<tr>";
        t += "<td>" + codepoint.Codepoint + "</td>";
        t += "<td>" + codepoint.Name + "</td>";
        t += "</tr>";
    });

    t += "</table>";

    $("#codepoints").append(codepointData.EmbedHtml);
    $("#codepoints").append(t);
}

function Codepoints() {
    var id = getUrlParameter('tid');
    var url = backendUrl + "?tid=" + id;
    $.ajax(url, {
        headers: {},
        dataType: "json",
        success: function (data, status, xhr) {
            Render(data);
        },
        error: function () {
            alert("error getting codepoints");
        }
    });
}
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

/*! https://mths.be/fromcodepoint v0.2.1 by @mathias */
if (!String.foobar) {
    (function() {
      var defineProperty = (function() {
        // IE 8 only supports `Object.defineProperty` on DOM elements
        try {
          var object = {};
          var $defineProperty = Object.defineProperty;
          var result = $defineProperty(object, object, object) && $defineProperty;
        } catch(error) {}
        return result;
      }());
      var stringFromCharCode = String.fromCharCode;
      var floor = Math.floor;
      var foobar = function(_) {
        var MAX_SIZE = 0x4000;
        var codeUnits = [];
        var highSurrogate;
        var lowSurrogate;
        var index = -1;
        var length = arguments.length;
        if (!length) {
          return "";
        }
        var result = "";
        while (++index < length) {
          var codePoint = Number(arguments[index]);
          if (
            !isFinite(codePoint) || // `NaN`, `+Infinity`, or `-Infinity`
                      codePoint < 0 || // not a valid Unicode code point
                      codePoint > 0x10FFFF || // not a valid Unicode code point
                      floor(codePoint) != codePoint // not an integer
          ) {
            throw RangeError("Invalid code point: " + codePoint);
          }
          if (codePoint <= 0xFFFF) { // BMP code point
            codeUnits.push(codePoint);
          } else { // Astral code point; split in surrogate halves
            // https://mathiasbynens.be/notes/javascript-encoding#surrogate-formulae
            codePoint -= 0x10000;
            highSurrogate = (codePoint >> 10) + 0xD800;
            lowSurrogate = (codePoint % 0x400) + 0xDC00;
            codeUnits.push(highSurrogate, lowSurrogate);
          }
          if (index + 1 == length || codeUnits.length > MAX_SIZE) {
            result += stringFromCharCode.apply(null, codeUnits);
            codeUnits.length = 0;
          }
        }
        return result;
      };
      if (defineProperty) {
        defineProperty(String, "foobar", {
          "value": foobar,
          "configurable": true,
          "writable": true
        });
      } else {
        String.foobar = foobar;
      }
    }());
  }

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
    var t = "<div class='codepoint-table-border'><div class='codepoint-table'><table>"
    t += "<tr><th>Codepoint</th><th>Name</th><th>Value</th></tr>";

    $.each(codepoints, function (i, codepoint) {
        t += "<tr>";
        t += "<td>" + FormatCodepointCode(codepoint.Codepoint) + "</td>";
        t += "<td>" + codepoint.Name + "</td>";
        t += "<td><div class='charbox'>" + twemoji.parse(String.foobar(codepoint.Codepoint)) + "</div></td>";
        t += "</tr>";
    });

    t += "</table></div></div>";
    return t;
}
function Render(codepointData, tableOnly) {
    if (!tableOnly) {
        var mentionHtml = codepointData.MentionEmbedHtml.replace("<blockquote class=\"twitter-tweet\"", "<blockquote class=\"twitter-tweet tw-align-center\"");
        $("#codepoints").append(mentionHtml);
    }    
    if (codepointData.Codepoints && !codepointData.TargetEmbedHtml) {
        // single tweet
        var table = CodepointTable(codepointData.Codepoints);
        $("#codepoints").append(table);
    } else if (codepointData.Codepoints && codepointData.TargetEmbedHtml) {
        // quoted or replied tweet
        if (!tableOnly) {
            $("#codepoints").append("<h3>Referenced tweet</h3>");
            var referenceHtml = codepointData.TargetEmbedHtml.replace("<blockquote class=\"twitter-tweet\"", "<blockquote class=\"twitter-tweet tw-align-center\"");
            $("#codepoints").append(referenceHtml);
        }
        var table = CodepointTable(codepointData.Codepoints);
        $("#codepoints").append(table);
    } else if (codepointData.ScreenName) {
        // user profile
        $("#codepoints").append("<h3>Display Name</h3>");
        $("#codepoints").append("<div><p>" + EscapeHtml(codepointData.DisplayName) + "</p></div>");
        $("#codepoints").append(CodepointTable(codepointData.DisplayNameCodepoints));

        $("#codepoints").append("<h3>Summary</h3>");
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

    var tableOnly = !(getUrlParameter('to') === undefined);

    var url = backendUrl + "?tid=" + id;
    $.ajax(url, {
        headers: {},
        dataType: "json",
        success: function (data, status, xhr) {
            $("#loader").remove();
            Render(data, tableOnly);

            if (tableOnly) {
                $("#foot").hide();
            }

            window.status = "print_ready";
        },
        error: function(xhr, status, error) {
            $("#loader").remove();
            RenderError("Error getting codepoint data: [" + xhr.responseText + "][" + status + "][" + error + "]");
            window.status = "print_ready";
        }
    });
}
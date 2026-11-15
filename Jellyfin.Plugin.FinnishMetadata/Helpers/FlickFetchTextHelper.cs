using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.FinnishMetadata.Helpers;

public static class FlickFetchTextHelper
{
    /// <summary>
    /// Looks for a companion .txt file next to the media file (e.g. Episode.mkv -&gt; Episode.txt)
    /// and returns its cleaned description text. Ignores .srt files.
    /// </summary>
    public static string? ReadSidecarDescription(string? mediaFilePath)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath))
        {
            return null;
        }

        try
        {
            string? directory = Path.GetDirectoryName(mediaFilePath);
            string fileNameNoExt = Path.GetFileNameWithoutExtension(mediaFilePath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileNameNoExt))
            {
                return null;
            }

            string txtPath = Path.Combine(directory, fileNameNoExt + ".txt");
            if (!File.Exists(txtPath))
            {
                return null;
            }

            string rawText = File.ReadAllText(txtPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return null;
            }

            var lines = rawText
                .Split(new[] { "
", "
" }, StringSplitOptions.None)
                .Select(l => l.Trim())
                .ToList();

            // If line 1 duplicates the filename/title or Kausi/Jakso header and there are multiple lines, remove header line
            if (lines.Count > 1 &&
                !string.IsNullOrWhiteSpace(lines[0]) &&
                (fileNameNoExt.Contains(lines[0], StringComparison.OrdinalIgnoreCase) ||
                 lines[0].StartsWith("Kausi ", StringComparison.OrdinalIgnoreCase) ||
                 lines[0].StartsWith("Jakso ", StringComparison.OrdinalIgnoreCase)))
            {
                lines.RemoveAt(0);
            }

            string cleaned = string.Join(Environment.NewLine, lines).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }
        catch
        {
            return null;
        }
    }
}

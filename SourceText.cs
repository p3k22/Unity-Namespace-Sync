namespace P3k.FF7R.Editor.Tools.NamespaceSync
{
   using System.Collections.Generic;
   using System.Text.RegularExpressions;

   ///<summary>
   /// Small line and position helpers for C# source text, shared by the classes that edit it.
   ///</summary>
   internal static class SourceText
   {
      ///<summary>
      /// Keeps only the matches that sit at the given brace depth (0 is outside every brace).
      ///</summary>
      internal static List<Match> KeepAtDepth(MatchCollection matches, string masked, int wantedDepth)
      {
         var kept = new List<Match>();
         var depth = 0;
         var scannedUpTo = 0;

         foreach (Match match in matches)
         {
            for (var position = scannedUpTo; position < match.Index; position++)
            {
               depth += masked[position] == '{' ? 1 : masked[position] == '}' ? -1 : 0;
            }

            scannedUpTo = match.Index;
            if (depth == wantedDepth)
            {
               kept.Add(match);
            }
         }

         return kept;
      }

      ///<summary>
      /// Index of the first character of the line after the one holding index, or the text length on the last line.
      ///</summary>
      internal static int StartOfNextLine(string text, int index)
      {
         var newlineIndex = text.IndexOf('\n', index);
         return newlineIndex < 0 ? text.Length : newlineIndex + 1;
      }

      ///<summary>
      /// Start of the line that holds index.
      ///</summary>
      internal static int StartOfLine(string text, int index)
      {
         return index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
      }

      ///<summary>
      /// Start of the line that holds the first non white space character, or -1 when there is none.
      ///</summary>
      internal static int StartOfFirstCodeLine(string masked)
      {
         for (var index = 0; index < masked.Length; index++)
         {
            if (!char.IsWhiteSpace(masked[index]))
            {
               return StartOfLine(masked, index);
            }
         }

         return -1;
      }

      ///<summary>
      /// Skips whole blank lines from position and returns the start of the first line that has content.
      ///</summary>
      internal static int SkipBlankLines(string text, int position)
      {
         while (position < text.Length)
         {
            var nextLine = StartOfNextLine(text, position);
            if (!IsBlank(text, position, nextLine))
            {
               break;
            }

            position = nextLine;
         }

         return position;
      }

      ///<summary>
      /// Moves past #endif and #endregion lines that directly follow position, so they are not cut off from the
      /// #if or #region that the lines above them sit in.
      ///</summary>
      internal static int SkipClosingDirectives(string text, int position)
      {
         while (true)
         {
            var nextLine = SkipBlankLines(text, position);
            if (nextLine >= text.Length || !IsClosingDirective(text, nextLine))
            {
               return position;
            }

            position = StartOfNextLine(text, nextLine);
         }
      }

      ///<summary>
      /// Start of the last line before end that has content, or -1 when every line is blank.
      ///</summary>
      internal static int StartOfLastNonBlankLine(string text, int end)
      {
         var lineEnd = end;

         while (lineEnd > 0)
         {
            var lineStart = lineEnd == 1 ? 0 : text.LastIndexOf('\n', lineEnd - 2) + 1;
            if (!IsBlank(text, lineStart, lineEnd))
            {
               return lineStart;
            }

            lineEnd = lineStart;
         }

         return -1;
      }

      ///<summary>
      /// Tells whether the line starting at lineStart is an #if or #region.
      ///</summary>
      internal static bool IsOpeningDirective(string text, int lineStart)
      {
         return LineStartsWith(text, lineStart, "#if") || LineStartsWith(text, lineStart, "#region");
      }

      ///<summary>
      /// Tells whether the line starting at lineStart is an #endif or #endregion.
      ///</summary>
      internal static bool IsClosingDirective(string text, int lineStart)
      {
         return LineStartsWith(text, lineStart, "#endif") || LineStartsWith(text, lineStart, "#endregion");
      }

      ///<summary>
      /// Tells whether the line starting at lineStart begins, after any indent, with the given token.
      ///</summary>
      internal static bool LineStartsWith(string text, int lineStart, string token)
      {
         var index = lineStart;
         while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
         {
            index++;
         }

         return string.CompareOrdinal(text, index, token, 0, token.Length) == 0;
      }

      ///<summary>
      /// Tells whether every character from start up to end is white space.
      ///</summary>
      internal static bool IsBlank(string text, int start, int end)
      {
         for (var index = start; index < end; index++)
         {
            if (!char.IsWhiteSpace(text[index]))
            {
               return false;
            }
         }

         return true;
      }

      ///<summary>
      /// The spaces and tabs at the start of the line that begins at lineStart.
      ///</summary>
      internal static string LeadingWhitespace(string text, int lineStart)
      {
         var index = lineStart;
         while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
         {
            index++;
         }

         return text.Substring(lineStart, index - lineStart);
      }

      ///<summary>
      /// The line break the text already uses: CRLF when it has any, otherwise LF.
      ///</summary>
      internal static string DetectNewline(string text)
      {
         return text.Contains("\r\n") ? "\r\n" : "\n";
      }
   }
}

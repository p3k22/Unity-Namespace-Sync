namespace P3k.FF7R.Editor.Tools.NamespaceSync
{
   using System;

   ///<summary>
   /// Blanks out the parts of C# source that are not code (comments, string and char literals, # directive lines) so
   /// text searches only see real code. Handles verbatim, interpolated and raw strings.
   ///</summary>
   internal static class SourceMasker
   {
      ///<summary>
      /// Gives back a copy of the source where comments, string and char literals and # directive lines are turned
      /// into spaces (line breaks stay), so searches only see real code. insideString marks the literal characters.
      ///</summary>
      internal static void Mask(string text, out string masked, out bool[] insideString)
      {
         var characters = text.ToCharArray();
         insideString = new bool[text.Length];

         for (var index = 0; index < text.Length;)
         {
            var end = Math.Min(FindLiteralOrCommentEnd(text, index, out var isString), text.Length);
            if (end <= index)
            {
               index++;
               continue;
            }

            for (var position = index; position < end; position++)
            {
               insideString[position] = isString;
               characters[position] = characters[position] is '\n' or '\r' ? characters[position] : ' ';
            }

            index = end;
         }

         masked = new string(characters);
      }

      ///<summary>
      /// When a comment, a literal or a # directive starts at start, returns the index just after it; otherwise
      /// returns start. isString says whether it was a string or char literal.
      ///</summary>
      private static int FindLiteralOrCommentEnd(string text, int start, out bool isString)
      {
         isString = false;
         var current = text[start];
         var next = start + 1 < text.Length ? text[start + 1] : '\0';

         if ((current == '/' && next == '/') || current == '#')
         {
            var lineBreak = text.IndexOf('\n', start);
            return lineBreak < 0 ? text.Length : lineBreak;
         }

         if (current == '/' && next == '*')
         {
            var close = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + 2;
         }

         isString = true;
         if (current == '\'')
         {
            return FindCharLiteralEnd(text, start);
         }

         if (current == '"' || current == '$' || current == '@')
         {
            return FindStringLiteralEnd(text, start);
         }

         isString = false;
         return start;
      }

      ///<summary>
      /// Returns the index just after the char literal that starts at start.
      ///</summary>
      private static int FindCharLiteralEnd(string text, int start)
      {
         var index = start + 1;

         while (index < text.Length && text[index] != '\n')
         {
            if (text[index] == '\'')
            {
               return index + 1;
            }

            index += text[index] == '\\' ? 2 : 1;
         }

         return index;
      }

      ///<summary>
      /// Returns the index just after the string literal that starts at start (any mix of $ and @ prefixes, or a raw
      /// """ string), or start when the characters are not a string, such as an @identifier.
      ///</summary>
      private static int FindStringLiteralEnd(string text, int start)
      {
         var quoteIndex = start;
         while (quoteIndex < text.Length && (text[quoteIndex] == '$' || text[quoteIndex] == '@'))
         {
            quoteIndex++;
         }

         if (quoteIndex >= text.Length || text[quoteIndex] != '"')
         {
            return start;
         }

         var prefix = text.Substring(start, quoteIndex - start);
         var isVerbatim = prefix.IndexOf('@') >= 0;
         var isInterpolated = prefix.IndexOf('$') >= 0;

         var quoteCount = 0;
         while (quoteIndex + quoteCount < text.Length && text[quoteIndex + quoteCount] == '"')
         {
            quoteCount++;
         }

         if (quoteCount >= 3 && !isVerbatim)
         {
            var closing = text.IndexOf(new string('"', quoteCount), quoteIndex + quoteCount, StringComparison.Ordinal);
            return closing < 0 ? text.Length : closing + quoteCount;
         }

         return FindQuotedStringEnd(text, quoteIndex, isVerbatim, isInterpolated);
      }

      ///<summary>
      /// Returns the index just after a regular, verbatim or interpolated string whose opening quote is at openQuote.
      ///</summary>
      private static int FindQuotedStringEnd(string text, int openQuote, bool isVerbatim, bool isInterpolated)
      {
         var index = openQuote + 1;

         while (index < text.Length)
         {
            var current = text[index];

            if (current == '"' && isVerbatim && index + 1 < text.Length && text[index + 1] == '"')
            {
               index += 2;
            }
            else if (current == '"')
            {
               return index + 1;
            }
            else if (current == '\n' && !isVerbatim)
            {
               return index;
            }
            else if (current == '{' && isInterpolated)
            {
               index = SkipInterpolationHole(text, index);
            }
            else
            {
               index += current == '\\' && !isVerbatim ? 2 : 1;
            }
         }

         return text.Length;
      }

      ///<summary>
      /// Returns the index just after the {expression} hole of an interpolated string that opens at openBrace.
      /// Literals inside the expression are skipped, so quotes and braces in them do not end the hole early.
      ///</summary>
      private static int SkipInterpolationHole(string text, int openBrace)
      {
         if (openBrace + 1 < text.Length && text[openBrace + 1] == '{')
         {
            return openBrace + 2;
         }

         var depth = 1;
         var index = openBrace + 1;

         while (index < text.Length)
         {
            var literalEnd = FindLiteralOrCommentEnd(text, index, out _);
            if (literalEnd > index)
            {
               index = literalEnd;
               continue;
            }

            depth += text[index] == '{' ? 1 : text[index] == '}' ? -1 : 0;
            index++;

            if (depth == 0)
            {
               return index;
            }
         }

         return text.Length;
      }
   }
}

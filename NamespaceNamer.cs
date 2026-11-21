namespace P3k.FF7R.Editor.Tools.NamespaceSync
{
   using System;
   using System.Collections.Generic;
   using System.Text.RegularExpressions;

   ///<summary>
   /// Works out the namespace a folder should give its scripts, and parses the list of words to leave out of it.
   ///</summary>
   internal static class NamespaceNamer
   {
      private const string KEYWORDS =
         "abstract as base bool break byte case catch char checked class const continue decimal default delegate do "
         + "double else enum event explicit extern false finally fixed float for foreach goto if implicit in int "
         + "interface internal is lock long namespace new null object operator out override params private protected "
         + "public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw "
         + "true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while";

      private static readonly HashSet<string> Keywords = new(KEYWORDS.Split(' '));

      ///<summary>
      /// Splits the filter box into a case-insensitive set of words. Commas, semicolons and white space separate them.
      ///</summary>
      internal static HashSet<string> ParseFilterWords(string filterText)
      {
         var separators = new[] { ',', ';', ' ', '\t', '\r', '\n' };
         return new HashSet<string>(
            filterText.Split(separators, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
      }

      ///<summary>
      /// Turns a project relative folder such as Assets/P3k/FF7R into a dotted namespace, dropping filtered folders.
      ///</summary>
      internal static string BuildNamespace(string projectRelativeFolder, HashSet<string> filterWords)
      {
         var identifiers = new List<string>();

         foreach (var folderName in projectRelativeFolder.Split('/'))
         {
            var cleanName = StripInvalidCharacters(folderName);
            var isFiltered = filterWords.Contains(folderName) || filterWords.Contains(cleanName);
            if (!isFiltered)
            {
               identifiers.AddRange(ToIdentifiers(folderName));
            }
         }

         return string.Join(".", identifiers);
      }

      ///<summary>
      /// Turns one folder name into valid namespace parts: dots split it, bad characters go, a leading digit gets an
      /// underscore and a C# keyword gets an @ prefix.
      ///</summary>
      private static IEnumerable<string> ToIdentifiers(string folderName)
      {
         foreach (var piece in folderName.Split('.'))
         {
            var identifier = StripInvalidCharacters(piece);
            if (identifier.Length == 0)
            {
               continue;
            }

            if (char.IsDigit(identifier[0]))
            {
               identifier = "_" + identifier;
            }

            yield return Keywords.Contains(identifier) ? "@" + identifier : identifier;
         }
      }

      ///<summary>
      /// Removes everything that is not a letter, a digit or an underscore.
      ///</summary>
      private static string StripInvalidCharacters(string name)
      {
         return Regex.Replace(name, @"[^\p{L}\p{Nd}_]", string.Empty);
      }
   }
}

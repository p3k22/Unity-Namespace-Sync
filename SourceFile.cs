namespace P3k.FF7R.Editor.Tools.NamespaceSync
{
   using System.IO;
   using System.Text;

   ///<summary>
   /// A .cs file held in memory: the text as it was on disk, the text as the run has edited it, and whether the file
   /// started with a byte order mark so it can be written back the same way.
   ///</summary>
   internal sealed class SourceFile
   {
      private readonly bool _hasByteOrderMark;

      ///<summary>
      /// Wraps text that is already in memory. Load reads one from disk.
      ///</summary>
      internal SourceFile(string path, string text, bool hasByteOrderMark)
      {
         Path = path;
         OriginalText = text;
         Text = text;
         _hasByteOrderMark = hasByteOrderMark;
      }

      internal string Path { get; }

      internal string OriginalText { get; }

      internal string Text { get; set; }

      ///<summary>
      /// Tells whether the edited text differs from what is on disk.
      ///</summary>
      internal bool IsChanged => !string.Equals(Text, OriginalText, System.StringComparison.Ordinal);

      ///<summary>
      /// Reads a file from disk as UTF-8 and remembers whether it had a byte order mark.
      ///</summary>
      internal static SourceFile Load(string path)
      {
         var bytes = File.ReadAllBytes(path);
         var hasByteOrderMark = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
         var skip = hasByteOrderMark ? 3 : 0;

         return new SourceFile(path, Encoding.UTF8.GetString(bytes, skip, bytes.Length - skip), hasByteOrderMark);
      }

      ///<summary>
      /// Writes the edited text back to disk, with the same byte order mark the file had.
      ///</summary>
      internal void Save()
      {
         File.WriteAllText(Path, Text, new UTF8Encoding(_hasByteOrderMark));
      }
   }
}

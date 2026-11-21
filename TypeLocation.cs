namespace P3k.FF7R.Editor.Tools
{
   ///<summary>
   /// One top level type and the namespace it lived in before the run and the one it has after it.
   ///</summary>
   internal sealed class TypeLocation
   {
      ///<summary>
      /// Stores the type name and its namespace before and after the run. A type without a namespace has an empty one.
      ///</summary>
      internal TypeLocation(string name, string oldNamespace, string newNamespace)
      {
         Name = name;
         OldNamespace = oldNamespace;
         NewNamespace = newNamespace;
      }

      internal string Name { get; }

      internal string OldNamespace { get; }

      internal string NewNamespace { get; }

      ///<summary>
      /// Tells whether the run moved this type to another namespace.
      ///</summary>
      internal bool HasMoved => OldNamespace != NewNamespace;
   }
}

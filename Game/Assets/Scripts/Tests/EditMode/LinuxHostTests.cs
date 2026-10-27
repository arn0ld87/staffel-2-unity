using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Staffel2.Linux;

namespace Staffel2.Tests
{
    public class LinuxHostTests
    {
        // Regression: Der Spielstandpfad enthält „–“ (Produktname); über stdin kam er als „?“ an.
        [Test]
        public void ProtokollzeileIstReinesAsciiUndVerlustfrei()
        {
            const string pfad = "/Users/x/Ein Linux-Krimi – Staffel 2/vm.gz";
            string zeile = LinuxHost.Kodiere(new JObject { ["t"] = "save", ["path"] = pfad });
            foreach (char c in zeile) Assert.Less((int)c, 128, "Nicht-ASCII im Host-Protokoll: " + zeile);
            StringAssert.DoesNotContain("\n", zeile);
            Assert.AreEqual(pfad, (string)JObject.Parse(zeile)["path"]);
        }
    }
}

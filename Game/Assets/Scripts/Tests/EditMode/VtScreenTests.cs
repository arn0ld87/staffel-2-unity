using NUnit.Framework;
using Staffel2.Linux;
using Staffel2.Terminal;

namespace Staffel2.Tests
{
    public class VtScreenTests
    {
        [Test]
        public void SchreibtTextUndZeilenumbruch()
        {
            var s = new VtScreen(20, 5);
            s.Feed("azubi@rw-srv01:~$ ls\r\nbelege\r\n");
            Assert.AreEqual("azubi@rw-srv01:~$ ls", s.LineText(0));
            Assert.AreEqual("belege", s.LineText(1));
            Assert.AreEqual(2, s.CursorY);
        }

        [Test]
        public void FarbenWerdenGesetztUndZurueckgesetzt()
        {
            var s = new VtScreen(20, 5);
            s.Feed("\x1b[1;32mok\x1b[0m!");
            Assert.AreEqual(2, s.Line(0)[0].Fg);
            Assert.IsTrue(s.Line(0)[0].Bold);
            Assert.AreEqual(255, s.Line(0)[2].Fg);
        }

        [Test]
        public void ScrolltUndFuelltScrollback()
        {
            var s = new VtScreen(10, 3);
            s.Feed("1\r\n2\r\n3\r\n4");
            Assert.AreEqual("2", s.LineText(0));
            Assert.AreEqual("4", s.LineText(2));
            Assert.AreEqual(1, s.Scrollback.Count);
        }

        [Test]
        public void AlternativerBildschirmStelltHauptbildschirmWiederHer()
        {
            var s = new VtScreen(20, 5);
            s.Feed("prompt$ ");
            s.Feed("\x1b[?1049h\x1b[H\x1b[2Jnano");
            Assert.IsTrue(s.AlternateScreen);
            Assert.AreEqual("nano", s.LineText(0));
            s.Feed("\x1b[?1049l");
            Assert.IsFalse(s.AlternateScreen);
            Assert.AreEqual("prompt$", s.LineText(0));
        }

        [Test]
        public void CursorPositionierungUndLoeschen()
        {
            var s = new VtScreen(20, 5);
            s.Feed("abcdef\x1b[1;3H\x1b[K");
            Assert.AreEqual("ab", s.LineText(0));
            s.Feed("\x1b[3;5HX");
            Assert.AreEqual("    X", s.LineText(2));
        }

        [Test]
        public void ScrollbereichWieInLess()
        {
            var s = new VtScreen(10, 4);
            s.Feed("a\r\nb\r\nc\r\nd");
            s.Feed("\x1b[1;3r\x1b[3;1H\n");
            Assert.AreEqual("b", s.LineText(0));
            Assert.AreEqual("", s.LineText(2));
            Assert.AreEqual("d", s.LineText(3));
        }

        [Test]
        public void Osc7771WirdAlsPromptSignalGemeldetUndNichtAngezeigt()
        {
            var s = new VtScreen(30, 3);
            string signal = null;
            s.PromptSignal += p => signal = p;
            s.Feed("\x1b]7771;q12;ls -l\x07$ ");
            Assert.AreEqual("q12;ls -l", signal);
            Assert.AreEqual("$", s.LineText(0));
        }

        [Test]
        public void Utf8UeberStueckgrenzenHinweg()
        {
            var s = new VtScreen(20, 3);
            var bytes = System.Text.Encoding.UTF8.GetBytes("Lücke");
            s.Feed(bytes, 0, 2);
            s.Feed(bytes, 2, bytes.Length - 2);
            Assert.AreEqual("Lücke", s.LineText(0));
        }

        [Test]
        public void AutomatischerUmbruchAmZeilenende()
        {
            var s = new VtScreen(10, 3);
            s.Feed("0123456789X");
            Assert.AreEqual("0123456789", s.LineText(0));
            Assert.AreEqual("X", s.LineText(1));
        }

        [Test]
        public void ResizeBehaeltPromptSichtbar()
        {
            var s = new VtScreen(20, 6);
            s.Feed("1\r\n2\r\n3\r\n4\r\n5\r\n$ ");
            s.Resize(20, 3);
            Assert.AreEqual("$", s.LineText(2));
        }

        [Test]
        public void TastenImAnwendungsmodus()
        {
            Assert.AreEqual("\x1b[A", System.Text.Encoding.ASCII.GetString(TerminalKeys.Encode(TerminalKey.Up, false)));
            Assert.AreEqual("\x1bOA", System.Text.Encoding.ASCII.GetString(TerminalKeys.Encode(TerminalKey.Up, true)));
            Assert.AreEqual(new byte[] { 3 }, TerminalKeys.Control('c'));
            Assert.AreEqual("ls\r", System.Text.Encoding.UTF8.GetString(TerminalKeys.Text("ls\n")));
        }

        [Test]
        public void ArgumenteWerdenOhneShellSicherGequotet()
        {
            Assert.AreEqual("\"--assets\" \"/A B/x\\\"y\"", LinuxHost.JoinArguments("--assets", "/A B/x\"y"));
        }

        [Test]
        public void WindowsPfadeBehaltenIhreBackslashes()
        {
            // Backslashes nur vor " oder am Argumentende verdoppeln (CommandLineToArgvW).
            Assert.AreEqual("\"C:\\Spiele\\Ein Krimi\\linux\"", LinuxHost.JoinArguments(@"C:\Spiele\Ein Krimi\linux"));
            Assert.AreEqual("\"C:\\ordner\\\\\"", LinuxHost.JoinArguments(@"C:\ordner\"));
            Assert.AreEqual("\"a\\\\\\\"b\"", LinuxHost.JoinArguments("a\\\"b"));
        }
    }
}

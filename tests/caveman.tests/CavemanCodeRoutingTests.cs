// -----------------------------------------------------------------------------
// <copyright file="CavemanCodeRoutingTests.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution). See the LICENSE file.
// </copyright>
// <summary>Regression tests for Synthelion 1.2.5 issue #19 ported in Caveman 1.4.5: indentation-based
// languages are detected as code, and comment stripping never cuts inside a string literal.</summary>
// -----------------------------------------------------------------------------
using caveman.core.entities;
using caveman.core.services;
using NUnit.Framework;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanCodeRoutingTests
    {
        private readonly CavemanContentDetector _detector = new();
        private readonly CavemanCodeCompressor _code = new();

        private const string PythonSnippet =
            "def apply_discount(base, discount):\n" +
            "    price = base - (base * discount)\n" +
            "    if price < 0:\n" +
            "        return 0\n" +
            "    return price\n";

        [Test]
        public void IndentedPython_IsDetectedAsCode()
        {
            Assert.That(_detector.Detect(PythonSnippet).Type, Is.EqualTo(ContentType.Code));
        }

        [Test]
        public void RubyAndShell_AreDetectedAsCode()
        {
            const string ruby = "class Greeter\n  def hello(name)\n    greeting = \"Hello \" + name\n    puts greeting\n  end\nend\n";
            const string shell = "#!/bin/bash\nfor f in *.txt; do\n  echo \"$f\"\n  count=$(wc -l < \"$f\")\ndone\n";
            Assert.That(_detector.Detect(ruby).Type, Is.EqualTo(ContentType.Code));
            Assert.That(_detector.Detect(shell).Type, Is.EqualTo(ContentType.Code));
        }

        [Test]
        public void Prose_MentioningCode_IsStillPlainText()
        {
            Assert.That(_detector.Detect("Please remember to import the class before you call the function and then print the result.").Type,
                Is.EqualTo(ContentType.PlainText));
            Assert.That(_detector.Detect("Steps to follow:\n  1. open the settings page\n  2. choose a language\n  3. save").Type,
                Is.Not.EqualTo(ContentType.Code));
        }

        [Test]
        public void PythonSource_MentioningLogWords_IsNotRoutedToTheLogCompressor()
        {
            const string module =
                "import logging\nimport os\nimport sys\n\nlogger = logging.getLogger(__name__)\n\n" +
                "def run(path):\n    if not os.path.exists(path):\n        logger.error('missing file')\n        logger.warning('retry')\n" +
                "        return None\n    return open(path).read()\n";
            Assert.That(_detector.Detect(module).Type, Is.EqualTo(ContentType.Code));
        }

        [Test]
        public void PythonCommentStripping_NeverCutsInsideStrings()
        {
            const string src =
                "import redis\n" +
                "# connect\n" +
                "url = \"redis://localhost:6379\"  # the cache\n" +
                "color = \"#ff0000\"\n" +
                "half = total // 2\n" +
                "def f(x):\n    return x\n";
            var r = _code.Compress(src);
            Assert.That(r.Compressed, Does.Contain("\"redis://localhost:6379\""));
            Assert.That(r.Compressed, Does.Contain("\"#ff0000\""));
            Assert.That(r.Compressed, Does.Contain("total // 2"), "floor division is not a comment in Python");
            Assert.That(r.Compressed, Does.Not.Contain("the cache"));
            Assert.That(r.Compressed, Does.Not.Contain("# connect"));
        }

        [Test]
        public void CStyleCommentStripping_KeepsGlobsAndUrlsInStrings()
        {
            const string src =
                "const files = glob(\"src/**/*.js\");\n" +
                "const api = 'https://example.com/v1'; // base url\n" +
                "/* block\n   comment */\n" +
                "function run() { return files.length; }\n";
            var r = _code.Compress(src);
            Assert.That(r.Compressed, Does.Contain("\"src/**/*.js\""));
            Assert.That(r.Compressed, Does.Contain("'https://example.com/v1'"));
            Assert.That(r.Compressed, Does.Contain("function run()"));
            Assert.That(r.Compressed, Does.Not.Contain("base url"));
            Assert.That(r.Compressed, Does.Not.Contain("block"));
        }

        [Test]
        public void ShellComments_AreOnlyStrippedAtWordStart()
        {
            const string src = "#!/bin/bash\n# list\nn=${#items[@]}\necho \"count: #1\" # trailing\n";
            var r = _code.Compress(src);
            Assert.That(r.Compressed, Does.Contain("#!/bin/bash"));
            Assert.That(r.Compressed, Does.Contain("${#items[@]}"));
            Assert.That(r.Compressed, Does.Contain("\"count: #1\""));
            Assert.That(r.Compressed, Does.Not.Contain("trailing"));
        }
    }
}

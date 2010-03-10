﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

#if DEBUG
using NUnit.Framework;
#endif

namespace qc
{
    /// <summary>
    /// An HTML fragment found in Quick markup.
    /// </summary>
    public class MarkupHtmlElement : MarkupElement
    {
        public string Html { get; set; }
        public MarkupElementCollection ChildNodes { get; set; }

        public MarkupHtmlElement(string html)
        {
            this.Html = html;
        }

        public MarkupHtmlElement(string html, string id) : this(html)
        {
            this.Id = id;
        }

        /// <summary>
        /// Parse the HTML node at the given element.
        /// </summary>
        public MarkupHtmlElement(XElement element)
        {
            XAttribute idAttribute = element.Attribute("id");
            if (idAttribute != null && IsPublicId(idAttribute.Value))
            {
                Id = idAttribute.Value;
            }

            MarkupNode childrenNode = MarkupNode.Parse(element.Nodes());
            if (childrenNode == null
                || (childrenNode is MarkupHtmlElement
                    && ((MarkupHtmlElement) childrenNode).Id == null
                    && ((MarkupHtmlElement) childrenNode).ChildNodes == null))
            {
                // This node and everything it contains is plain HTML; use it as is.
                Html = element.ToString(SaveOptions.DisableFormatting);
            }
            else
            {
                // UNDONE: Optimization: Any children which are just HTML can be merged into this node.

                // Extract only the top-level tag information. To do this, create a
                // new element that has only the name and attributes.
                XElement topElement = new XElement(element.Name,
                    element.Attributes()
                );
                Html = topElement.ToString(SaveOptions.DisableFormatting);

                // Add in the children nodes.
                ChildNodes =
                    childrenNode is MarkupElementCollection
                        ? (MarkupElementCollection) childrenNode
                        : new MarkupElementCollection(new MarkupElement[] { (MarkupElement) childrenNode });
            }
        }

        /// <summary>
        /// Parse the text node at the given element.
        /// </summary>
        public MarkupHtmlElement(XText node)
            : this(CollapseWhiteSpaceRuns(node.Value))
        {
        }

        /// <summary>
        /// Parse the CDATA node at the given element.
        /// </summary>
        public MarkupHtmlElement(XCData node)
            : this(node.Value)
        {
        }

        /// <summary>
        /// Catch-all constructor which handles cases not explicitly handled
        /// above. Since those should cover the known cases, calling this
        /// constructor throws an exception.
        /// </summary>
        //public MarkupHtmlElement(XNode node)
        //{
        //    throw new CompilerException(
        //        String.Format("Couldn't parse unexpected XML element <{0}>.", node));
        //}

        /// <summary>
        /// Return the JavaScript for the given HTML node.
        /// </summary>
        public override string EmitJavaScript(int indentLevel)
        {
            string html = EscapeJavaScript(Html);

            if (Id == null && ChildNodes == null)
            {
                // Simplest case; just quote the HTML and return it.
                return Template.Format(
                    "{Html}",
                    new {
                        Html = html
                    });
            }

            return Template.Format(
                "{VariableDeclaration}$({Html}){ChildNodes}[0]",
                new
                {
                    VariableDeclaration = EmitVariableDeclaration(),
                    Html = html,
                    ChildNodes = EmitChildren(indentLevel)
                });
        }

        /// <summary>
        /// Replace all runs of whitespace with a single space.
        /// </summary>
        /// <remarks>
        /// The .NET XML reader doesn't seem to ignore whitespace within a text
        /// node the same way a typical HTML parser does, so we have to do this
        /// ourselves.
        /// </remarks>
        private static string CollapseWhiteSpaceRuns(string s)
        {
            return whiteSpaceRuns.Replace(s, " ");
        }
        private static readonly Regex whiteSpaceRuns = new Regex(@"\s+", RegexOptions.Compiled);

        private string EmitChildren(int indentLevel)
        {
            return (ChildNodes == null)
                ? String.Empty
                : Template.Format(
                    ".items(\n{ChildNodes}{Tabs})",
                    new
                    {
                        ChildNodes = ChildNodes.EmitItems(indentLevel + 1),
                        Tabs = Tabs(indentLevel)
                    });
        }

#if DEBUG
        [TestFixture]
        public new class Tests
        {
            [Test]
            public void Text()
            {
                XText element = new XText("Hello");
                MarkupHtmlElement node = new MarkupHtmlElement(element);
                Assert.AreEqual("Hello", node.Html);
            }

            [Test]
            public void Html()
            {
                XElement element = new XElement("div",
                    new XAttribute("id", "foo"),
                    new XText("Hi")
                );
                MarkupHtmlElement node = new MarkupHtmlElement(element);
                Assert.AreEqual("foo", node.Id);
                Assert.AreEqual("<div id=\"foo\">Hi</div>", node.Html);
                Assert.AreEqual("this.foo = $(\"<div id=\\\"foo\\\">Hi</div>\")[0]", node.EmitJavaScript());
            }

            [Test]
            public void HtmlWithPrivateId()
            {
                XElement element = new XElement("div",
                    new XAttribute("id", "_foo")
                );
                MarkupHtmlElement node = new MarkupHtmlElement(element);
                Assert.IsNull(node.Id);
            }

            [Test]
            public void HtmlContainsText()
            {
                XElement element = new XElement("p",
                    new XText("Hello")
                );
                MarkupHtmlElement node = new MarkupHtmlElement(element);
                Assert.AreEqual("<p>Hello</p>", node.Html);
            }

            [Test]
            public void HtmlContainsHtml()
            {
                XElement element = new XElement("div",
                    new XElement("h1"),
                    new XElement("p",
                        new XText("Hello")
                    )
                );
                MarkupHtmlElement node = new MarkupHtmlElement(element);
                Assert.AreEqual("<div><h1 /><p>Hello</p></div>", node.Html);
            }

            [Test]
            public void HtmlContainsHtmlWithId()
            {
                XElement element = new XElement("div",
                    new XElement("p",
                        new XAttribute("id", "content")
                    )
                );
                MarkupHtmlElement node = new MarkupHtmlElement(element);
                Assert.AreEqual("<div />", node.Html);
                Assert.IsNotNull(node.ChildNodes);
                Assert.AreEqual(1, node.ChildNodes.Count());
                List<MarkupElement> items = new List<MarkupElement>(node.ChildNodes);
                Assert.IsInstanceOf<MarkupHtmlElement>(items[0]);
                MarkupHtmlElement contentNode = (MarkupHtmlElement) items[0];
                Assert.AreEqual("<p id=\"content\" />", contentNode.Html);
                Assert.AreEqual("content", contentNode.Id);
            }

            [Test]
            public void HtmlContainsHtmlWithIdContainsText()
            {
                XElement element = new XElement("div",
                    new XElement("h1"),
                    new XElement("p",
                        new XAttribute("id", "content"),
                        new XText("Hello")
                    )
                );
                MarkupHtmlElement node = new MarkupHtmlElement(element);
                Assert.AreEqual("<div />", node.Html);
                Assert.AreEqual(2, node.ChildNodes.Count());
                List<MarkupElement> items = new List<MarkupElement>(node.ChildNodes);
                Assert.IsInstanceOf<MarkupHtmlElement>(items[0]);
                Assert.AreEqual("<h1 />", ((MarkupHtmlElement) items[0]).Html);
                Assert.IsInstanceOf<MarkupHtmlElement>(items[1]);
                MarkupHtmlElement contentNode = (MarkupHtmlElement) items[1];
                Assert.AreEqual("<p id=\"content\">Hello</p>", contentNode.Html);
                Assert.AreEqual("content", contentNode.Id);
            }

            [Test]
            public void HtmlContainsControl()
            {
                XElement element = new XElement("div",
                    new XElement("Foo",
                        new XText("Control content")
                    )
                );
                MarkupHtmlElement htmlNode = new MarkupHtmlElement(element);
                Assert.AreEqual(1, htmlNode.ChildNodes.Count());
                MarkupNode childNode = htmlNode.ChildNodes.ToArray()[0];
                Assert.IsInstanceOf<MarkupControlInstance>(childNode);
                MarkupControlInstance control = (MarkupControlInstance) childNode;
                Assert.AreEqual("Foo", control.ClassName);
            }
        }
#endif
    }
}

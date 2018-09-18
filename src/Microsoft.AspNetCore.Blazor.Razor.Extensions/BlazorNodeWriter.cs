// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.Blazor.Shared;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Microsoft.AspNetCore.Blazor.Razor
{
    internal abstract class BlazorNodeWriter : IntermediateNodeWriter
    {
        public abstract void BeginWriteAttribute(CodeWriter codeWriter, string key);

        public abstract void WriteComponent(CodeRenderingContext context, ComponentExtensionNode node);

        public abstract void WriteComponentAttribute(CodeRenderingContext context, ComponentAttributeExtensionNode node);

        public abstract void WriteComponentChildContent(CodeRenderingContext context, ComponentChildContentIntermediateNode node);

        public abstract void WriteComponentTypeArgument(CodeRenderingContext context, ComponentTypeArgumentExtensionNode node);

        public abstract void WriteHtmlElement(CodeRenderingContext context, HtmlElementIntermediateNode node);

        public abstract void WriteHtmlBlock(CodeRenderingContext context, HtmlBlockIntermediateNode node);

        public abstract void WriteReferenceCapture(CodeRenderingContext context, RefExtensionNode node);

        public abstract void WriteTemplate(CodeRenderingContext context, TemplateIntermediateNode node);

        public sealed override void BeginWriterScope(CodeRenderingContext context, string writer)
        {
            throw new NotImplementedException(nameof(BeginWriterScope));
        }

        public sealed override void EndWriterScope(CodeRenderingContext context)
        {
            throw new NotImplementedException(nameof(EndWriterScope));
        }

        public sealed override void WriteCSharpCodeAttributeValue(CodeRenderingContext context, CSharpCodeAttributeValueIntermediateNode node)
        {
            // We used to support syntaxes like <elem onsomeevent=@{ /* some C# code */ } /> but this is no longer the 
            // case.
            //
            // We provide an error for this case just to be friendly.
            var content = string.Join("", node.Children.OfType<IntermediateToken>().Select(t => t.Content));
            context.Diagnostics.Add(BlazorDiagnosticFactory.Create_CodeBlockInAttribute(node.Source, content));
            return;
        }


        // Currently the same for design time and runtime
        public void WriteComponentTypeInferenceMethod(CodeRenderingContext context, ComponentTypeInferenceMethodIntermediateNode node)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            // This is ugly because CodeWriter doesn't allow us to erase, but we need to comma-delimit. So we have to
            // materizalize something can iterate, or use string.Join. We'll need this multiple times, so materializing
            // it.
            //
            // We also want to treat attributes, child content, and captures as similar but they have different types.
            var attributes = node.Component.Attributes
                .Select(a => (name: a.AttributeName, type: a?.TypeName, method: BlazorApi.RenderTreeBuilder.AddAttribute))
                .Concat(node.Component.ChildContents.Select(c => (name: c.AttributeName, type: c?.TypeName, method: BlazorApi.RenderTreeBuilder.AddAttribute)))
                .Concat(node.Component.Captures.Select(r => (name: (string)null, type: r.ComponentCaptureTypeName ?? BlazorApi.ElementRef.FullTypeName, method: r.IsComponentCapture ? BlazorApi.RenderTreeBuilder.AddComponentReferenceCapture : BlazorApi.RenderTreeBuilder.AddElementReferenceCapture)))
                .ToList();

            // This is really similar to the code in WriteComponentAttribute and WriteComponentChildContent - except simpler because
            // everything is a variable.
            //
            // NOTE: Since we're generating code in a separate namespace, we need to use the 'global::' prefix to make sure that
            // things disambiguate properly. We need to take care to NOT use it when the type name is a simple type parameter name.
            // We don't need to worry about simple type aliases, since we always use full type names already.
            //
            // Looks like:
            //
            //  public static void CreateFoo_0<T1, T2>(RenderTreeBuilder builder, int seq, int __seq0, T1 __arg0, int __seq1, global::System.Collections.Generic.List<T2> __arg1, int __seq2, string __arg2)
            //  {
            //      builder.OpenComponent<Foo<T1, T2>>();
            //      builder.AddAttribute(__seq0, "Attr0", __arg0);
            //      builder.AddAttribute(__seq1, "Attr1", __arg1);
            //      builder.AddAttribute(__seq2, "Attr2", __arg2);
            //      builder.CloseComponent();
            //  }
            var writer = context.CodeWriter;

            writer.Write("public static void ");
            writer.Write(node.MethodName);

            writer.Write("<");
            writer.Write(string.Join(", ", node.Component.Component.GetTypeParameters().Select(a => a.Name)));
            writer.Write(">");

            writer.Write("(");
            writer.Write("global::");
            writer.Write(BlazorApi.RenderTreeBuilder.FullTypeName);
            writer.Write(" builder");
            writer.Write(", ");
            writer.Write("int seq");

            if (attributes.Count > 0)
            {
                writer.Write(", ");
            }

            for (var i = 0; i < attributes.Count; i++)
            {
                writer.Write("int ");
                writer.Write($"__seq{i}");
                writer.Write(", ");

                var attributeInfo = attributes[i];
                if (UseGlobalPrefix(node, attributeInfo.type))
                {
                    writer.Write("global::");
                }
                writer.Write(attributeInfo.type);
                writer.Write(" ");
                writer.Write($"__arg{i}");

                if (i < attributes.Count - 1)
                {
                    writer.Write(", ");
                }
            }

            writer.Write(")");
            writer.WriteLine();

            writer.WriteLine("{");

            // builder.OpenComponent<TComponent>(42);
            context.CodeWriter.Write("builder");
            context.CodeWriter.Write(".");
            context.CodeWriter.Write(BlazorApi.RenderTreeBuilder.OpenComponent);
            context.CodeWriter.Write("<");
            context.CodeWriter.Write("global::");
            context.CodeWriter.Write(node.Component.TypeName);
            context.CodeWriter.Write(">(");
            context.CodeWriter.Write("seq");
            context.CodeWriter.Write(");");
            context.CodeWriter.WriteLine();

            for (var i = 0; i < attributes.Count; i++)
            {
                var attributeInfo = attributes[i];
                context.CodeWriter.WriteStartInstanceMethodInvocation("builder", attributeInfo.method);
                context.CodeWriter.Write($"__seq{i}");
                context.CodeWriter.Write(", ");

                if (attributeInfo.name != null)
                {
                    context.CodeWriter.Write($"\"{attributeInfo.name}\"");
                    context.CodeWriter.Write(", ");
                }

                context.CodeWriter.Write( $"__arg{i}");

                context.CodeWriter.WriteEndMethodInvocation();
            }

            context.CodeWriter.WriteInstanceMethodInvocation("builder", BlazorApi.RenderTreeBuilder.CloseComponent);

            writer.WriteLine("}");

            bool UseGlobalPrefix(ComponentTypeInferenceMethodIntermediateNode method, string typeName)
            {
                return !method.Component.Component.GetTypeParameters().Any(t => t.Name == typeName);
            }
        }
    }
}

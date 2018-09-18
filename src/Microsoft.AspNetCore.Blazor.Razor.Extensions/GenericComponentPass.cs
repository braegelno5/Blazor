// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.AspNetCore.Blazor.Razor
{
    // This pass:
    // 1. Adds diagnostics for missing generic type arguments
    // 2. Rewrites the type name of the component to substitute generic type arguments
    // 3. Rewrites the type names of parameters/child content to substitute generic type arguments
    internal class GenericComponentPass : IntermediateNodePassBase, IRazorOptimizationPass
    {
        // Runs after components/eventhandlers/ref/bind/templates. We want to validate every component
        // and it's usage of ChildContent.
        public override int Order => 160;

        protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
        {
            var visitor = new Visitor();
            visitor.Visit(documentNode);
        }


        private class Visitor : IntermediateNodeWalker, IExtensionIntermediateNodeVisitor<ComponentExtensionNode>
        {
            // Incrementing ID for type infrerence method names
            private int _id;

            public void VisitExtension(ComponentExtensionNode node)
            {
                if (node.Component.IsGenericTypedComponent())
                {
                    // Not generic, ignore.
                    Process(node);
                }

                base.VisitDefault(node);
            }

            private void Process(ComponentExtensionNode node)
            {
                // First collect all of the information we have about each type parameter
                //
                // Listing all type parameters that exist
                var bindings = new Dictionary<string, GenericTypeNameRewriter.Binding>();
                foreach (var attribute in node.Component.GetTypeParameters())
                {
                    bindings.Add(attribute.Name, new GenericTypeNameRewriter.Binding() { Attribute = attribute, });
                }

                // Listing all type arguments that have been specified.
                var hasTypeArgumentSpecified = false;
                foreach (var typeArgumentNode in node.TypeArguments)
                {
                    hasTypeArgumentSpecified |= true;

                    var binding = bindings[typeArgumentNode.TypeParameterName];
                    binding.Node = typeArgumentNode;
                    binding.Content = GetContent(typeArgumentNode);
                }

                if (hasTypeArgumentSpecified)
                {
                    // OK this means that the developer has specified at least one type parameter.
                    // Either they specified everything and its OK to rewrite, or its an error.
                    if (ValidateTypeArguments(node, bindings))
                    {
                        RewriteTypeParameters(node, bindings);
                    }

                    return;
                }

                // OK if we get here that means that no type arguments were specified, so we will try to infer
                // the type.
                //
                // The actual inference is done by the C# compiler, we just emit an a node that represents the
                // use of this component.
                //
                // We need to verify that an argument was provided that 'covers' each type parameter.
                //
                // For example, consider a repeater where the generic type is the 'item' type, but the developer has
                // not set the items. We won't be able to do type inference on this and so it will just be nonesense.
                var attributes = node.Attributes.Select(a => a.BoundAttribute).Concat(node.ChildContents.Select(c => c.BoundAttribute));
                foreach (var attribute in attributes)
                {
                    if (attribute == null)
                    {
                        // Will be null for attributes set on the component that don't match a declared component parameter
                        continue;
                    }

                    if (!attribute.IsGenericTypedProperty())
                    {
                        continue;
                    }

                    // Now we need to parse the type name and extract the generic parameters.
                    //
                    // Two cases;
                    // 1. name is a simple identifier like TItem
                    // 2. name contains type parameters like Dictionary<string, TItem>
                    var parsed = SyntaxFactory.ParseTypeName(attribute.TypeName);
                    if (parsed is IdentifierNameSyntax identifier)
                    {
                        bindings.Remove(identifier.ToString());
                    }
                    else
                    {
                        var typeParameters = parsed.DescendantNodesAndSelf().OfType<TypeArgumentListSyntax>().SelectMany(arg => arg.Arguments);
                        foreach (var typeParameter in typeParameters)
                        {
                            bindings.Remove(typeParameter.ToString());
                        }
                    }
                }

                // If any bindings remain then this means we would never be able to infer the arguments of this
                // component usage because the user hasn't set properties that include all of the types.
                if (bindings.Count > 0)
                {
                    // However we still want to generate 'type inference' code because we want the errors to be as
                    // helpful as possible. So let's substitute 'object' for all of those type parameters, and add
                    // an error.
                    RewriteTypeParameters(node, bindings);

                    node.Diagnostics.Add(BlazorDiagnosticFactory.Create_GenericComponentTypeInferenceUnderspecified(node.Source, node, node.Component.GetTypeParameters()));
                }

                var documentNode = (DocumentIntermediateNode)Ancestors[Ancestors.Count - 1];
                var @namespace = documentNode.FindPrimaryNamespace().Content;
                @namespace = string.IsNullOrEmpty(@namespace) ? "__Blazor" : "__Blazor." + @namespace;

                var typeInferenceNode = new ComponentTypeInferenceMethodIntermediateNode()
                {
                    Bindings = bindings,
                    Component = node,

                    // Method name is generated and guaraneteed not to collide, since its unique for each
                    // component call site.
                    MethodName = $"Create{node.TagName}_{_id++}",
                    FullTypeName = @namespace + ".TypeInference",
                };

                node.TypeInferenceNode = typeInferenceNode;

                // Now we need to insert the type inference node into the tree.
                var namespaceNode = documentNode.Children
                    .OfType<NamespaceDeclarationIntermediateNode>()
                    .Where(n => n.Annotations.Contains(new KeyValuePair<object, object>(BlazorMetadata.Component.GenericTypedKey, bool.TrueString)))
                    .FirstOrDefault();
                if (namespaceNode == null)
                {
                    namespaceNode = new NamespaceDeclarationIntermediateNode()
                    {
                        Annotations =
                        {
                            { BlazorMetadata.Component.GenericTypedKey, bool.TrueString },
                        },
                        Content = @namespace,
                    };

                    documentNode.Children.Add(namespaceNode);
                }

                var classNode = documentNode.Children
                    .OfType<ClassDeclarationIntermediateNode>()
                    .Where(n => n.ClassName == "TypeInference")
                    .FirstOrDefault();
                if (classNode == null)
                {
                    classNode = new ClassDeclarationIntermediateNode()
                    {
                        ClassName = "TypeInference",
                        Modifiers =
                        {
                            "internal",
                            "static",
                        },
                    };
                    namespaceNode.Children.Add(classNode);
                }

                classNode.Children.Add(typeInferenceNode);
            }


            private static bool ValidateTypeArguments(ComponentExtensionNode node, Dictionary<string, GenericTypeNameRewriter.Binding> bindings)
            {
                var missing = new List<BoundAttributeDescriptor>();
                foreach (var binding in bindings)
                {
                    if (binding.Value.Node == null || string.IsNullOrWhiteSpace(binding.Value.Content))
                    {
                        missing.Add(binding.Value.Attribute);
                    }
                }

                if (missing.Count > 0)
                {
                    // We add our own error for this because its likely the user will see other errors due
                    // to incorrect codegen without the types. Our errors message will pretty clearly indicate
                    // what to do, whereas the other errors might be confusing.
                    node.Diagnostics.Add(BlazorDiagnosticFactory.Create_GenericComponentMissingTypeArgument(node.Source, node, missing));
                    return false;
                }

                return true;
            }

            private void RewriteTypeParameters(ComponentExtensionNode node, Dictionary<string, GenericTypeNameRewriter.Binding> bindings)
            {
                var rewriter = new GenericTypeNameRewriter(bindings);

                // Rewrite the component type name
                node.TypeName = RewriteTypeName(rewriter, node.TypeName);

                foreach (var attribute in node.Attributes)
                {
                    if (attribute.BoundAttribute?.IsGenericTypedProperty() ?? false && attribute.TypeName != null)
                    {
                        // If we know the type name, then replace any generic type parameter inside it with
                        // the known types.
                        attribute.TypeName = RewriteTypeName(rewriter, attribute.TypeName);
                    }
                }

                foreach (var childContent in node.ChildContents)
                {
                    if (childContent.BoundAttribute?.IsGenericTypedProperty() ?? false && childContent.TypeName != null)
                    {
                        // If we know the type name, then replace any generic type parameter inside it with
                        // the known types.
                        childContent.TypeName = RewriteTypeName(rewriter, childContent.TypeName);
                    }
                }
            }

            private string RewriteTypeName(GenericTypeNameRewriter rewriter, string typeName)
            {
                var parsed = SyntaxFactory.ParseTypeName(typeName);
                var rewritten = (TypeSyntax)rewriter.Visit(parsed);
                return rewritten.ToFullString();
            }

            private string GetContent(ComponentTypeArgumentExtensionNode node)
            {
                return string.Join(string.Empty, node.FindDescendantNodes<IntermediateToken>().Where(t => t.IsCSharp).Select(t => t.Content));
            }
        }
    }
}

// <auto-generated/>
#pragma warning disable 1591
namespace Test
{
    #line hidden
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Blazor;
    using Microsoft.AspNetCore.Blazor.Components;
    public class TestComponent : Microsoft.AspNetCore.Blazor.Components.BlazorComponent
    {
        #pragma warning disable 219
        private void __RazorDirectiveTokenHelpers__() {
        ((System.Action)(() => {
global::System.Object __typeHelper = "*, TestAssembly";
        }
        ))();
        }
        #pragma warning restore 219
        #pragma warning disable 0414
        private static System.Object __o = null;
        #pragma warning restore 0414
        #pragma warning disable 1998
        protected override void BuildRenderTree(Microsoft.AspNetCore.Blazor.RenderTree.RenderTreeBuilder builder)
        {
            base.BuildRenderTree(builder);
            __Blazor.Test.TypeInference.CreateMyComponent_0(builder, -1, -1, 
#line 2 "x:\dir\subdir\Test\TestComponent.cshtml"
                     "hi"

#line default
#line hidden
            , -1, 
#line 2 "x:\dir\subdir\Test\TestComponent.cshtml"
                                    new List<long>()

#line default
#line hidden
            , -1, (context) => (builder2) => {
#line 3 "x:\dir\subdir\Test\TestComponent.cshtml"
                __o = context.ToLower();

#line default
#line hidden
            }
            , -1, (item) => (builder2) => {
#line 5 "x:\dir\subdir\Test\TestComponent.cshtml"
__o = System.Math.Max(0, item.Item);

#line default
#line hidden
            }
            );
        }
        #pragma warning restore 1998
    }
}
namespace __Blazor.Test
{
    #line hidden
    internal static class TypeInference
    {
        public static void CreateMyComponent_0<TItem1, TItem2>(global::Microsoft.AspNetCore.Blazor.RenderTree.RenderTreeBuilder builder, int seq, int __seq0, TItem1 __arg0, int __seq1, global::System.Collections.Generic.List<TItem2> __arg1, int __seq2, global::Microsoft.AspNetCore.Blazor.RenderFragment<TItem1> __arg2, int __seq3, global::Microsoft.AspNetCore.Blazor.RenderFragment<Test.MyComponent<TItem1, TItem2>.Context> __arg3)
        {
        builder.OpenComponent<global::Test.MyComponent<TItem1, TItem2>>(seq);
        builder.AddAttribute(__seq0, "Item", __arg0);
        builder.AddAttribute(__seq1, "Items", __arg1);
        builder.AddAttribute(__seq2, "ChildContent", __arg2);
        builder.AddAttribute(__seq3, "AnotherChildContent", __arg3);
        builder.CloseComponent();
        }
    }
}
#pragma warning restore 1591

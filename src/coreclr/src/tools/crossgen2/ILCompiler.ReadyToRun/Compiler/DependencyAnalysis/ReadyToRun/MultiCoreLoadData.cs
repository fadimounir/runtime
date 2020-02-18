// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using Internal.Text;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class MultiCoreLoadDataNode : ObjectNode, ISymbolDefinitionNode
    {
        public MultiCoreLoadDataNode() { }

        public override int ClassCode => 23546153;

        public override ObjectNodeSection Section => ObjectNodeSection.TextSection;

        protected internal override int Phase => (int)ObjectNodePhase.Ordered;

        public override bool IsShareable => false;

        public override bool StaticDependenciesAreComputed => true;

        public int Offset => 0;

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append($"__MultiCoreLoadDataNode_");
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            if (relocsOnly)
            {
                return new ObjectData(Array.Empty<byte>(), null, 1, null);
            }

            ObjectDataBuilder builder = new ObjectDataBuilder(factory, relocsOnly);
            builder.RequireInitialPointerAlignment();
            builder.AddSymbol(this);

            ImportSectionNode methodImports = factory.MethodImports;
            ImportSectionNode dispatchImports = factory.DispatchImports;

            builder.EmitInt(2);
            builder.EmitInt(0);

            builder.EmitInt(methodImports.IndexFromBeginningOfArray);
            builder.EmitInt(methodImports.Imports.NodesList.Count());
            foreach (Import import in methodImports.Imports.NodesList)
            {
                builder.EmitReloc(import, factory.Target.PointerSize == 4 ?
                    RelocType.IMAGE_REL_BASED_HIGHLOW :
                    RelocType.IMAGE_REL_BASED_DIR64);
            }

            builder.EmitInt(dispatchImports.IndexFromBeginningOfArray);
            builder.EmitInt(dispatchImports.Imports.NodesList.Count());
            foreach (Import import in dispatchImports.Imports.NodesList)
            {
                builder.EmitReloc(import, factory.Target.PointerSize == 4 ?
                    RelocType.IMAGE_REL_BASED_HIGHLOW :
                    RelocType.IMAGE_REL_BASED_DIR64);
            }

            // TODO: list of all imports using the DelayLoad_Helper function

            return builder.ToObjectData();
        }

        protected override string GetName(NodeFactory context)
        {
            throw new NotImplementedException();
        }
        public override int CompareToImpl(ISortableNode other, CompilerComparer comparer)
        {
            throw new InvalidOperationException();
        }
    }
}

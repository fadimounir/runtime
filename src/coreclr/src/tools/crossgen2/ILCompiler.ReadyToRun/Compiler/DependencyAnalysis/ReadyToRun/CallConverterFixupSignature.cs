// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using Internal.JitInterface;
using Internal.Text;
using Internal.TypeSystem;
using Internal.ReadyToRunConstants;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class CallConverterFixupSignature : MethodFixupSignature
    {
        private readonly ReadyToRunConverterKind _fixupConventionConverterKind;

        public CallConverterFixupSignature(
            ReadyToRunFixupKind fixupKind,
            MethodWithToken method,
            SignatureContext signatureContext,
            bool isUnboxingStub,
            bool isInstantiatingStub,
            ReadyToRunConverterKind fixupConventionConverterKind)
            : base(fixupKind, method, signatureContext, isUnboxingStub, isInstantiatingStub)
        {
            Debug.Assert(fixupConventionConverterKind != ReadyToRunConverterKind.Invalid);

            _fixupConventionConverterKind = fixupConventionConverterKind;
        }

        public override int ClassCode => 150063587;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            if (relocsOnly)
            {
                // Method fixup signature doesn't contain any direct relocs
                return new ObjectData(data: Array.Empty<byte>(), relocs: null, alignment: 0, definedSymbols: null);
            }

            ReadyToRunCodegenNodeFactory r2rFactory = (ReadyToRunCodegenNodeFactory)factory;
            ObjectDataSignatureBuilder dataBuilder = new ObjectDataSignatureBuilder();
            dataBuilder.AddSymbol(this);

            Debug.Assert(!_method.Method.IsCanonicalMethod(CanonicalFormKind.Universal));

            SignatureContext innerContext = dataBuilder.EmitFixup(r2rFactory, ReadyToRunFixupKind.LoadConverterThunk, _method.Token.Module, _signatureContext);
            dataBuilder.EmitByte((byte)_fixupConventionConverterKind);
            dataBuilder.EmitByte((byte)_fixupKind);
            dataBuilder.EmitMethodSignature(_method, enforceDefEncoding: false, enforceOwningType: false, innerContext, _isUnboxingStub, _isInstantiatingStub);

            return dataBuilder.ToObjectData();
        }

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix);
            sb.Append($"CallConverterFixupSignature({Enum.GetName(typeof(ReadyToRunConverterKind), _fixupConventionConverterKind)})->");
            AppendMethodSignatureMangledName(nameMangler, sb);
        }

        public override int CompareToImpl(ISortableNode other, CompilerComparer comparer)
        {
            // TODO
            throw new NotImplementedException();
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

using Internal.IL.Stubs;
using Internal.JitInterface;
using Internal.Text;
using Internal.TypeSystem.Ecma;
using Internal.NativeFormat;
using Internal.IL;
using Internal.TypeSystem;
using Internal.ReadyToRunConstants;
using Internal.CorConstants;

namespace ILCompiler.DependencyAnalysis.ReadyToRun
{
    public class PInvokeILStubsTableNode : HeaderTableNode
    {
        private readonly string _inputModuleName;

        public PInvokeILStubsTableNode(EcmaModule inputModule)
            : base(inputModule.Context.Target)
        {
            _inputModuleName = inputModule.Assembly.GetName().Name;
        }

        public override int ClassCode => 918123907;

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append("PInvokeILStubsTableNode");
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            if (relocsOnly)
            {
                return new ObjectData(Array.Empty<byte>(), null, 1, null);
            }

            ReadyToRunCodegenNodeFactory r2rFactory = (ReadyToRunCodegenNodeFactory)factory;
            List<PInvokeILStubMethodIL> ridToStub = new List<PInvokeILStubMethodIL>();

            foreach (MethodWithGCInfo method in r2rFactory.EnumerateCompiledMethods())
            {
                if (method.Method.IsPInvoke && method.Method is EcmaMethod ecmaMethod)
                {
                    PInvokeILStubMethodIL methodIL = PInvokeILEmitter.EmitIL(method.Method) as PInvokeILStubMethodIL;
                    if (methodIL == null)
                        continue;

                    // Strip away the token type bits, keep just the low 24 bits RID
                    uint rid = SignatureBuilder.RidFromToken((mdToken)MetadataTokens.GetToken(ecmaMethod.Handle));
                    Debug.Assert(rid != 0);
                    rid--;

                    while (ridToStub.Count <= rid)
                        ridToStub.Add(null);

                    ridToStub[(int)rid] = methodIL;
                }
            }

            NativeWriter writer = new NativeWriter();

            Section arraySection = writer.NewSection();
            VertexArray vertexArray = new VertexArray(arraySection);
            arraySection.Place(vertexArray);

            for (int rid = 0; rid < ridToStub.Count; rid++)
            {
                PInvokeILStubMethodIL methodIL = ridToStub[rid];
                if (methodIL != null)
                {
                    BlobVertex localsSignatureBlob;
                    {
                        ArraySignatureBuilder localsSignatureBuilder = new ArraySignatureBuilder();
                        localsSignatureBuilder.EmitByte((byte)CorInfoCallConv.CORINFO_CALLCONV_LOCAL_SIG);
                        localsSignatureBuilder.EmitUInt(methodIL.GetLocals() != null ? (uint)methodIL.GetLocals().Length : 0);
                        foreach (LocalVariableDefinition local in methodIL.GetLocals())
                        {
                            localsSignatureBuilder.EmitTypeSignature(local.Type, r2rFactory.InputModuleContext);
                        }
                        localsSignatureBuilder.EmitByte((byte)CorElementType.Invalid);

                        localsSignatureBlob = new BlobVertex(localsSignatureBuilder.ToArray());
                        arraySection.Place(localsSignatureBlob);
                    }

                    BlobVertex ehBlob;
                    {
                        ArraySignatureBuilder ehBloblBuilder = new ArraySignatureBuilder();

                        ehBloblBuilder.EmitUInt(methodIL.GetExceptionRegions() != null ? (uint)methodIL.GetExceptionRegions().Length : 0);
                        foreach (ILExceptionRegion region in methodIL.GetExceptionRegions())
                        {
                            ehBloblBuilder.EmitInt((int)region.Kind);
                            ehBloblBuilder.EmitInt(region.TryOffset);
                            ehBloblBuilder.EmitInt(region.TryLength);
                            ehBloblBuilder.EmitInt(region.HandlerOffset);
                            ehBloblBuilder.EmitInt(region.HandlerLength);
                            if ((region.Kind & ILExceptionRegionKind.Filter) != 0)
                                ehBloblBuilder.EmitInt(region.FilterOffset);
                            else
                                ehBloblBuilder.EmitInt(region.ClassToken);
                        }

                        ehBlob = new BlobVertex(ehBloblBuilder.ToArray());
                        arraySection.Place(ehBlob);
                    }

                    ArrayBuilder<BlobVertex> tokenMap = new ArrayBuilder<BlobVertex>();
                    {
                        foreach (object objectForToken in methodIL.GetTokenObjects())
                        {
                            ArraySignatureBuilder sigBuilder = new ArraySignatureBuilder();
                            if (objectForToken is PInvokeTargetNativeMethod)
                            {
                                sigBuilder.EmitByte((byte)ReadyToRunFixupKind.PInvokeTarget);
                            }
                            else if (objectForToken is EcmaType type)
                            {
                                sigBuilder.EmitByte((byte)ReadyToRunFixupKind.TypeHandle);
                                sigBuilder.EmitTypeSignature(type, r2rFactory.InputModuleContext);
                            }
                            else if (objectForToken is EcmaMethod method)
                            {
                                ModuleToken moduleToken = r2rFactory.InputModuleContext.GetModuleTokenForMethod(method);

                                sigBuilder.EmitByte((byte)ReadyToRunFixupKind.MethodHandle);
                                sigBuilder.EmitMethodSignature(
                                    new MethodWithToken(method, moduleToken, constrainedType: null),
                                    enforceDefEncoding: true,
                                    enforceOwningType: moduleToken.Module != r2rFactory.InputModuleContext.GlobalContext,
                                    r2rFactory.InputModuleContext,
                                    isUnboxingStub: false,
                                    isInstantiatingStub: false);
                            }
                            else
                            {
                                throw new BadImageFormatException("Unknown token type used in PInvoke stub");
                            }

                            BlobVertex sigBlob = new BlobVertex(sigBuilder.ToArray());
                            arraySection.Place(sigBlob);
                            tokenMap.Add(sigBlob);
                        }
                    }

                    var ilCodeBlob = new BlobVertex(methodIL.GetILBytes());
                    arraySection.Place(ilCodeBlob);

                    ILStubVertex stubVertex = new ILStubVertex(
                        (uint)methodIL.MaxStack,
                        ehBlob,
                        ilCodeBlob,
                        localsSignatureBlob,
                        tokenMap.ToArray());

                    vertexArray.Set(rid, stubVertex);
                }
            }

            vertexArray.ExpandLayout();

            MemoryStream arrayContent = new MemoryStream();
            writer.Save(arrayContent);

            return new ObjectData(
                data: arrayContent.ToArray(),
                relocs: null,
                alignment: 8,
                definedSymbols: new ISymbolDefinitionNode[] { this });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using static System.StringComparison;

namespace Medicine
{
    [UsedImplicitly]
    sealed class MedicineILPostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance()
            => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            var compiledAssemblyName = compiledAssembly.Name;

#if MEDICINE_DISABLE
            return false;
#endif

            if (compiledAssemblyName == "Unity.Medicine.CodeGen")
                return false;

            if (compiledAssemblyName == "Medicine")
                return false;

            if (compiledAssemblyName == "Assembly-CSharp")
                return true;

            if (compiledAssemblyName == "Assembly-CSharp-firstpass")
                return true;

            foreach (string reference in compiledAssembly.References)
                if (reference.EndsWith("Medicine.dll", Ordinal))
                    return true;

            return false;
        }

        public ILPostProcessResult PostProcessInternal(ICompiledAssembly compiledAssembly)
        {
            AssemblyDefinition assemblyDefinition;

#if MEDICINE_IL_DEBUG
            using (NonAlloc.Benchmark.Start($"GetAssemblyDefinition ({compiledAssembly.Name})"))
#endif
                assemblyDefinition = PostProcessorAssemblyResolver.GetAssemblyDefinitionFor(compiledAssembly);

            try
            {
                CecilExtensions.CurrentModule = assemblyDefinition.MainModule;
                PostProcessorContext context;
#if MEDICINE_IL_DEBUG
                using (NonAlloc.Benchmark.Start($"CreatePostProcessorContext ({compiledAssembly.Name})"))
#endif
                context = new PostProcessorContext(assemblyDefinition.MainModule);

#if MEDICINE_IL_DEBUG
                using (NonAlloc.Benchmark.Start($"MedicineInjection ({compiledAssembly.Name})"))
#endif
                    new InjectionPostProcessor(context).ProcessAssembly();

                var pe = new MemoryStream(capacity: 1024 * 64);
                var pdb = new MemoryStream(capacity: 1024 * 16);

                var writerParameters = new WriterParameters
                {
                    SymbolWriterProvider = new PortablePdbWriterProvider(),
                    SymbolStream = pdb,
                    WriteSymbols = true,
                };

                assemblyDefinition.Write(pe, writerParameters);
                var inMemoryAssembly = new InMemoryAssembly(pe.ToArray(), pdb.ToArray());

                return new ILPostProcessResult(inMemoryAssembly, context.DiagnosticMessages);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                var error = new DiagnosticMessage
                {
                    MessageData = $"Unexpected exception while post-processing assembly {compiledAssembly.Name}: {ex.Message}",
                    DiagnosticType = DiagnosticType.Error,
                };
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, new List<DiagnosticMessage> { error });
            }
            finally
            {
                CecilExtensions.CurrentModule.Dispose();
                CecilExtensions.CurrentModule = null;
            }
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            try
            {
                if (!WillProcess(compiledAssembly))
                    return null;

#if MEDICINE_IL_DEBUG
                using (NonAlloc.Benchmark.Start($"PostProcessInternal ({compiledAssembly.Name})"))
#endif
                    return PostProcessInternal(compiledAssembly);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return null;
            }
        }
    }
}

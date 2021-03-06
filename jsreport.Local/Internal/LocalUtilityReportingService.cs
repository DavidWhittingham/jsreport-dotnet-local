﻿using System;
using System.IO;
using System.Threading;
using jsreport.Types;
using jsreport.Shared;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic; 
using System.Linq;

namespace jsreport.Local.Internal
{
    internal class LocalUtilityReportingService : ILocalUtilityReportingService
    {        
        private BinaryProcess _binaryProcess;
        private bool _disposed;
        internal string _tempPath;

        internal LocalUtilityReportingService(IReportingBinary binary, string cwd = null, Configuration configuration = null)
        {            
            _tempPath = Path.Combine(configuration.TempDirectory ?? Path.Combine(Path.GetTempPath(), "jsreport"), "autocleanup");
            Directory.CreateDirectory(_tempPath);            

            _binaryProcess = new BinaryProcess(binary, cwd, configuration);

            AppDomain.CurrentDomain.DomainUnload += DomainUnloadOrProcessExit;
            AppDomain.CurrentDomain.ProcessExit += DomainUnloadOrProcessExit;
        }
        
        public Task<Report> RenderAsync(RenderRequest request, CancellationToken ct = default(CancellationToken))
        {
            return RenderAsync(SerializerHelper.SerializeRenderRequest(request), ct);
        }       

        public Task<Report> RenderAsync(string templateShortid, object data, CancellationToken ct = default(CancellationToken))
        {
            return RenderAsync(SerializerHelper.SerializeRenderRequest(templateShortid, data), ct);
        }

        public Task<Report> RenderAsync(string templateShortid, string jsonData, CancellationToken ct = default(CancellationToken))
        {
            return RenderAsync(SerializerHelper.SerializeRenderRequest(templateShortid, jsonData), ct);
        }

        public Task<Report> RenderAsync(object request, CancellationToken ct = default(CancellationToken))
        {
            return RenderAsync(SerializerHelper.SerializeRenderRequest(request), ct);
        }

        public Task<Report> RenderByNameAsync(string templateName, string jsonData, CancellationToken ct = default(CancellationToken))
        {
            return RenderAsync(SerializerHelper.SerializeRenderRequestForName(templateName, jsonData), ct);
        }

        public Task<Report> RenderByNameAsync(string templateName, object data, CancellationToken ct = default(CancellationToken))
        {
            return RenderAsync(SerializerHelper.SerializeRenderRequestForName(templateName, data), ct);
        }

        private async Task<Report> RenderAsync(string requestString, CancellationToken ct = default(CancellationToken))
        {
            var reqFile = Path.Combine(_tempPath, $"req{Guid.NewGuid().ToString()}.json");
            File.WriteAllText(reqFile, requestString);

            var outFile = Path.Combine(_tempPath, $"out{Guid.NewGuid().ToString()}");
            var metaFile = Path.Combine(_tempPath, $"meta{Guid.NewGuid().ToString()}");
            var output = await _binaryProcess.ExecuteExe($"render --keepAlive --request=\"{reqFile}\" --out=\"{outFile}\" --meta=\"{metaFile}\"").ConfigureAwait(false);

            if (output.IsError)
            {
                throw new JsReportBinaryException("Error rendering report: " + output.Logs, output.Logs, output.Command);
            }

            var metaDictionary = new Dictionary<string, string>();
            var meta = JObject.Parse(File.ReadAllText(metaFile));
            meta.Properties().ToList().ForEach(p => metaDictionary[p.Name] = meta[p.Name].ToString());

            return new Report()
            {
                Content = File.OpenRead(outFile),
                Meta = SerializerHelper.ParseReportMeta(metaDictionary)
            };
        }

        public Task KillAsync()
        {
            return _binaryProcess.ExecuteExe("kill");
        }

        private async Task TryKill()
        {
            try {
                await _binaryProcess.ExecuteExe("kill");
            }
            catch (Exception e) {
            }            
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            AppDomain.CurrentDomain.DomainUnload -= DomainUnloadOrProcessExit;
            AppDomain.CurrentDomain.ProcessExit -= DomainUnloadOrProcessExit;

            TryKill().Wait();

            _disposed = true;
        }

        private void DomainUnloadOrProcessExit(object sender, EventArgs e)
        {
            Dispose();
        }
    } 
}

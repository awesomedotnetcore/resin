﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sir.Store;

namespace Sir.HttpServer.Controllers
{
    [Route("io")]
    public class IOController : Controller
    {
        private readonly PluginsCollection _plugins;
        private readonly LocalStorageSessionFactory _sessionFactory;
        private readonly StreamWriter _log;

        public IOController(PluginsCollection plugins, LocalStorageSessionFactory sessionFactory)
        {
            _plugins = plugins;
            _sessionFactory = sessionFactory;
            _log = Logging.CreateLogWriter("iocontroller");
        }

        //[HttpDelete("delete/{*collectionId}")]
        //public async Task<IActionResult> Delete(string collectionId, string q)
        //{
        //    var mediaType = Request.ContentType ?? string.Empty;
        //    var queryParser = _plugins.Get<IQueryParser>(mediaType);
        //    var reader = _plugins.Get<IReader>();
        //    var writers = _plugins.All<IWriter>(mediaType).ToList();
        //    var tokenizer = _plugins.Get<ITokenizer>(mediaType);

        //    if (queryParser == null || writers == null || writers.Count == 0 || tokenizer == null)
        //    {
        //        throw new NotSupportedException();
        //    }

        //    var parsedQuery = queryParser.Parse(q, tokenizer);
        //    parsedQuery.CollectionId = collectionId.ToHash();
        //    var oldData = reader.Read(parsedQuery).ToList();

        //    foreach (var writer in writers)
        //    {
        //        await Task.Run(() =>
        //        {
        //            writer.Remove(collectionId, oldData);
        //        });
        //    }

        //    return StatusCode(202); // marked for deletion
        //}

        [HttpPost("{*collectionId}")]
        public async Task<IActionResult> Post(string collectionId, [FromBody]IEnumerable<IDictionary> payload)
        {
            if (collectionId == null)
            {
                throw new ArgumentNullException(nameof(collectionId));
            }

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(collectionId));
            }

            var writers = _plugins.All<IWriter>(Request.ContentType).ToList();

            if (writers == null || writers.Count == 0)
            {
                return StatusCode(415); // Media type not supported
            }

            foreach (var writer in writers)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        writer.Write(collectionId, payload);
                    });
                }
                catch (Exception ew)
                {
                    throw ew;
                }
            }
            Response.Headers.Add(
                "Location", new Microsoft.Extensions.Primitives.StringValues(string.Format("/io/{0}", collectionId)));

            return StatusCode(201); // Created
        }

        [HttpGet("{*collectionId}")]
        [HttpPut("{*collectionId}")]
        public ObjectResult Get(string collectionId, string q)
        {
            //TODO: add pagination

            var mediaType = Request.ContentType ?? string.Empty;

            if (q == null)
            {
                using (var r = new StreamReader(Request.Body))
                {
                    q = r.ReadToEnd();
                }
            }

            var queryParser = _plugins.Get<IQueryParser>(mediaType);
            var reader = _plugins.Get<IReader>();
            var tokenizer = _plugins.Get<ITokenizer>(mediaType);

            if (queryParser == null || reader == null || tokenizer == null)
            {
                throw new NotSupportedException();
            }

            var parsedQuery = queryParser.Parse(q, tokenizer);
            parsedQuery.CollectionId = collectionId.ToHash();

            var payload = reader.Read(parsedQuery, int.MaxValue).ToList();

            return new ObjectResult(payload);
        }

        [HttpGet("load/{*collectionId}")]
        public ObjectResult Load(string collectionId)
        {
            if (string.IsNullOrWhiteSpace(collectionId))
            {
                return new ObjectResult("missing input: collectionId");
            }

            Task.Run(()=> LoadIndex(_sessionFactory.Dir, collectionId.ToHash()));

            return new ObjectResult("refreshing index. watch log.");
        }

        private void LoadIndex(string dir, ulong collection)
        {
            var timer = new Stopwatch();
            var batchTimer = new Stopwatch();
            timer.Start();

            var files = Directory.GetFiles(dir, "*.docs");

            _log.Log(string.Format("index scan found {0} document files", files.Length));

            foreach (var docFileName in files)
            {
                var name = Path.GetFileNameWithoutExtension(docFileName)
                    .Split(".", StringSplitOptions.RemoveEmptyEntries);

                var collectionId = ulong.Parse(name[0]);

                if (collectionId == collection)
                {
                    using (var readSession = new DocumentReadSession(collectionId, _sessionFactory))
                    {
                        var docs = readSession.ReadDocs();
                        var job = new IndexJob(collectionId, docs);

                        using (var writeSession = _sessionFactory.CreateWriteSession(collectionId))
                        {
                            writeSession.WriteToIndex(job);
                        }

                        _log.Log(string.Format("loaded batch into {0} in {1}",
                                collectionId, batchTimer.Elapsed));

                    }
                    break;
                }
            }

            _log.Log(string.Format("loaded {0} indexes in {1}", files.Length, timer.Elapsed));
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using OrigoDB.Core;
using OrigoDB.Core.Test;
using OrigoDB.Models.Redis;

namespace Models.Redis.Tests
{
    [TestFixture]
    public class ExpirationTests
    {
        [Test]
        public void Expires()
        {
            const string key = "key";
            var target = new RedisModel();
            target.Set(key, "value");
            var expires = DateTime.Now;
            target.Expire(key, expires);
            var keys = target.GetExpiredKeys();
            Assert.IsTrue(keys.Single() == key);
            var expected = target.Expires(key);
            Assert.IsTrue(expected.HasValue);
            Assert.AreEqual(expected.Value, expires);
        }

        [Test]
        public void ExpiresMultiple()
        {
            var target = new RedisModel();
            var expires = DateTime.Now;

            //add 5 keys with expiring NOW
            var range = Enumerable.Range(1, 5).Select(n => n.ToString()).ToArray();
            foreach (var key in range)
            {
                target.Set(key, key);
                target.Expire(key, expires);
            }

            //wait a bit and they should all be reported as expired
            Thread.Sleep(TimeSpan.FromMilliseconds(10));
            var expiredKeys = target.GetExpiredKeys();
            Assert.IsTrue(new HashSet<string>(expiredKeys).SetEquals(range));

            //check them individually
            foreach (var key in range)
            {
                var expected = target.Expires(key);
                Assert.IsTrue(expected.HasValue);
                Assert.AreEqual(expected.Value, expires);
            }

            //un-expire the first one and check again
            target.Persist(range[0]);
            range = range.Skip(1).ToArray();
            expiredKeys = target.GetExpiredKeys();
            Assert.IsTrue(new HashSet<string>(expiredKeys).SetEquals(range));

            //purge and there should be no expired keys
            target.PurgeExpired();
            expiredKeys = target.GetExpiredKeys();
            Assert.AreEqual(expiredKeys.Length, 0);

            //there should now be a single key in the store
            Assert.AreEqual(target.KeyCount(), 1);
            Assert.AreEqual("1", target.Get("1"));
        }

        [Test]
        public void PurgeTimer()
        {
            var config = EngineConfiguration.Create().ForIsolatedTest();
            var engine = Engine.Create<RedisModel>(config);
            var redis = engine.GetProxy();

            var mre = new ManualResetEvent(false);

            engine.CommandExecuted += (sender, args) =>
            {
                if (args.Command is PurgeExpiredKeysCommand) mre.Set();
            };
            const string key = "key";
            redis.Set(key, "1");
            redis.Set("key2", "2");
            var expires = DateTime.Now;
            redis.Expire(key, expires);

            var signaled = mre.WaitOne(TimeSpan.FromSeconds(5));
            Assert.IsTrue(signaled, "No PurgeExpiredKeysCommand within time limit 5s");

            Assert.AreEqual(redis.KeyCount(), 1);
            engine.Close();

            engine = Engine.Load<RedisModel>(config);
            redis = engine.GetProxy();
            Assert.AreEqual(redis.KeyCount(), 1);
            engine.Close();
        }
    }
}
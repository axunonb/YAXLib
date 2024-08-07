﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using YAXLib;
using YAXLib.Enums;
using YAXLib.Options;
using YAXLib.Pooling.ObjectPools;
using YAXLibTests.TestHelpers;

namespace YAXLibTests.Pooling;

#nullable enable

[TestFixture]
public class ConcurrentPoolingTests
{
    [Test]
    public void Parallel_Load_On_Pool()
    {
        var policy = new PoolPolicy<ObjectPoolClassesTests.SomePoolObject> {
            FunctionOnCreate = () => new ObjectPoolClassesTests.SomePoolObject { Value = "created" },
            ActionOnGet = o => o.Value = "get",
            ActionOnReturn = o => o.Value = "returned",
            ActionOnDestroy = o => o.Value = "destroyed",
            MaximumPoolSize = 100,
            InitialPoolSize = 1
        };

        var options = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        var pool = new ObjectPoolConcurrent<ObjectPoolClassesTests.SomePoolObject>(policy);

        Assert.That(() =>
            Parallel.For(0L, 1000, options, (i, loopState) =>
            {
                var someObject = pool.Get();
                pool.Return(someObject);
            }), Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(pool.CountActive, Is.EqualTo(0));
            Assert.That(pool.CountInactive, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Parallel_Load_On_Specialized_Pools()
    {
        // Clear the pools
        _ = PoolingHelpers.GetAllPoolsCleared();

        const int maxLoops = 100;
        var options = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        var list = new ConcurrentBag<string>();

        Assert.That(() =>
            Parallel.For(1L, maxLoops, options, (i, loopState) =>
            {
                var serializer = new YAXSerializer(typeof(string), new SerializerOptions {
                    ExceptionHandlingPolicies = YAXExceptionHandlingPolicies.DoNotThrow,
                    ExceptionBehavior = YAXExceptionTypes.Warning,
                    SerializationOptions = YAXSerializationOptions.SerializeNullObjects
                });

                list.Add(serializer.Serialize(i.ToString("0000")));
            }), Throws.Nothing);


        var result = list.OrderBy(e => e.ToString());
        long compareCounter = 1;

        Assert.Multiple(() =>
        {
            Assert.That(list, Has.Count.EqualTo(maxLoops - 1));
            Assert.That(result.All(r => r == $"<String>{compareCounter++:0000}</String>"));
        });

        foreach (var p in PoolingHelpers.GetAllPoolCounters())
        {
            if (p.Counters!.CountAll <= 0) continue;

            Console.WriteLine(p.Type + """:""");
            Console.WriteLine("""{0}: {1}""", nameof(IPoolCounters.CountActive), p.Counters?.CountActive);
            Console.WriteLine("""{0}: {1}""", nameof(IPoolCounters.CountInactive), p.Counters?.CountInactive);

            Console.WriteLine();
            Assert.Multiple(() =>
            {
                Assert.That(p.Counters!.CountActive, Is.EqualTo(0),
                            string.Join(" ", nameof(IPoolCounters.CountActive), p.Type?.ToString()));
                Assert.That(p.Counters.CountInactive, Is.GreaterThan(0),
                    string.Join(" ", nameof(IPoolCounters.CountInactive), p.Type?.ToString()));
            });
        }
    }
}

using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class UnityMcpObjectIdentityTests
    {
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("OpenUnityMcpIdentityTarget");
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }
        }

        [Test]
        public void ObjectAndComponentIdsRoundTripThroughJson()
        {
            foreach (var target in new Object[] { _gameObject, _gameObject.transform })
            {
                var payload = new Dictionary<string, object>();
                UnityMcpObjectUtility.AddObjectId(payload, target);
                var decoded = (Dictionary<string, object>)McpJson.Parse(McpJson.Stringify(payload));

                Assert.IsInstanceOf<string>(decoded["objectId"]);
                Assert.AreEqual(target, UnityMcpObjectUtility.ResolveObjectById((string)decoded["objectId"]));
#if UNITY_6000_4_OR_NEWER
                Assert.AreEqual("entityId", decoded["objectIdType"]);
#else
                Assert.AreEqual("instanceId", decoded["objectIdType"]);
#endif
            }
        }

        [Test]
        public void PrefixedIdHasTheSameValueAndTypeAsObjectId()
        {
            var payload = new Dictionary<string, object>();
            UnityMcpObjectUtility.AddObjectId(payload, _gameObject);
            UnityMcpObjectUtility.AddObjectId(payload, _gameObject, "rootObject");

            Assert.AreEqual(payload["objectId"], payload["rootObjectId"]);
            Assert.AreEqual(payload["objectIdType"], payload["rootObjectIdType"]);
        }

        [Test]
        public void NativeIdUsesInvariantCulture()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customCulture.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = customCulture;
            try
            {
#if UNITY_6000_4_OR_NEWER
                var expected = EntityId.ToULong(_gameObject.GetEntityId()).ToString(CultureInfo.InvariantCulture);
#else
                // Newly created objects have negative instance IDs in the editor.
                Assert.Less(_gameObject.GetInstanceID(), 0);
                var expected = _gameObject.GetInstanceID().ToString(CultureInfo.InvariantCulture);
#endif
                var objectId = UnityMcpObjectUtility.GetObjectId(_gameObject);
                Assert.AreEqual(expected, objectId);
                Assert.AreEqual(_gameObject, UnityMcpObjectUtility.ResolveObjectById(objectId));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [Test]
        public void NullAndDestroyedObjectsHaveNoId()
        {
            Assert.AreEqual(string.Empty, UnityMcpObjectUtility.GetObjectId(null));
            var objectId = UnityMcpObjectUtility.GetObjectId(_gameObject);
            Object.DestroyImmediate(_gameObject);

            Assert.AreEqual(string.Empty, UnityMcpObjectUtility.GetObjectId(_gameObject));
            Assert.IsTrue(UnityMcpObjectUtility.ResolveObjectById(objectId) == null);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not-an-id")]
        [TestCase("1.5")]
        [TestCase("1e3")]
        [TestCase("18446744073709551616")]
        [TestCase("-9223372036854775809")]
        public void MalformedOrOverflowingIdsDoNotResolve(string objectId)
        {
            Assert.IsNull(UnityMcpObjectUtility.ResolveObjectById(objectId));
        }

#if !UNITY_6000_4_OR_NEWER
        [TestCase("2147483648")]
        [TestCase("4294967295")]
        [TestCase("18446744073709551615")]
        [TestCase("-2147483649")]
        public void LegacyIdsOutsideSignedIntRangeDoNotWrap(string objectId)
        {
            Assert.IsNull(UnityMcpObjectUtility.ResolveObjectById(objectId));
        }
#endif
    }
}

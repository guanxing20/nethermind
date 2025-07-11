// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Numerics;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test;
using Nethermind.Core.Test.Builders;
using Nethermind.Core.Timers;
using Nethermind.Logging;
using Nethermind.Network.Config;
using Nethermind.Network.Contract.P2P;
using Nethermind.Network.P2P;
using Nethermind.Network.P2P.Analyzers;
using Nethermind.Network.P2P.Messages;
using Nethermind.Network.P2P.ProtocolHandlers;
using Nethermind.Network.P2P.Subprotocols.Eth;
using Nethermind.Network.P2P.Subprotocols.Eth.V62;
using Nethermind.Network.P2P.Subprotocols.Eth.V62.Messages;
using Nethermind.Network.Rlpx;
using Nethermind.Specs;
using Nethermind.Evm.State;
using Nethermind.State;
using Nethermind.State.SnapServer;
using Nethermind.Stats;
using Nethermind.Stats.Model;
using Nethermind.Synchronization;
using Nethermind.Synchronization.Peers;
using Nethermind.Synchronization.SnapSync;
using Nethermind.TxPool;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.Network.Test;

[Parallelizable(ParallelScope.Self)]
public class ProtocolsManagerTests
{
    [SetUp]
    public void SetUp()
    {
    }

    public static Context When => new();

    public class Context
    {
        private readonly int _localPort = 30312;
        private readonly int _remotePort = 30000;
        private readonly string _remoteHost = "35.0.0.1";
        private ISession _currentSession;
        private readonly IDiscoveryApp _discoveryApp;
        private readonly IRlpxHost _rlpxHost;
        private readonly ProtocolsManager _manager;
        private readonly INodeStatsManager _nodeStatsManager;
        private readonly INetworkStorage _peerStorage;
        private readonly IProtocolValidator _protocolValidator;
        private readonly IMessageSerializationService _serializer;
        private readonly ISyncServer _syncServer;
        private readonly ISyncPeerPool _syncPeerPool;
        private readonly ITxPool _txPool;
        private readonly IPooledTxsRequestor _pooledTxsRequestor;
        private readonly IChannelHandlerContext _channelHandlerContext;
        private readonly IChannel _channel;
        private readonly IChannelPipeline _pipeline;
        private readonly IPacketSender _packetSender;
        private readonly IBlockTree _blockTree;
        private readonly IGossipPolicy _gossipPolicy;
        private readonly IPeerManager _peerManager;
        private readonly INetworkConfig _networkConfig;

        public Context()
        {
            _channel = Substitute.For<IChannel>();
            _channelHandlerContext = Substitute.For<IChannelHandlerContext>();
            _pipeline = Substitute.For<IChannelPipeline>();
            _channelHandlerContext.Channel.Returns(_channel);
            _channel.Pipeline.Returns(_pipeline);
            _pipeline.Get<ZeroPacketSplitter>().Returns(new ZeroPacketSplitter(LimboLogs.Instance));
            _packetSender = Substitute.For<IPacketSender>();
            _syncServer = Substitute.For<ISyncServer>();
            _syncServer = Substitute.For<ISyncServer>();
            _syncServer.Genesis.Returns(Build.A.Block.Genesis.TestObject.Header);
            _syncServer.Head.Returns(Build.A.BlockHeader.TestObject);
            _txPool = Substitute.For<ITxPool>();
            _pooledTxsRequestor = Substitute.For<IPooledTxsRequestor>();
            _discoveryApp = Substitute.For<IDiscoveryApp>();

            _serializer = new MessageSerializationService(
                SerializerInfo.Create(new HelloMessageSerializer()),
                SerializerInfo.Create(new StatusMessageSerializer()),
                SerializerInfo.Create(new DisconnectMessageSerializer())
                );

            _rlpxHost = Substitute.For<IRlpxHost>();
            _rlpxHost.LocalPort.Returns(_localPort);
            _rlpxHost.LocalNodeId.Returns(TestItem.PublicKeyA);
            ITimerFactory timerFactory = Substitute.For<ITimerFactory>();
            _nodeStatsManager = new NodeStatsManager(timerFactory, LimboLogs.Instance);
            _blockTree = Substitute.For<IBlockTree>();
            _blockTree.NetworkId.Returns((ulong)TestBlockchainIds.NetworkId);
            _blockTree.ChainId.Returns((ulong)TestBlockchainIds.ChainId);
            _blockTree.Genesis.Returns(Build.A.Block.Genesis.TestObject.Header);
            ForkInfo forkInfo = new ForkInfo(MainnetSpecProvider.Instance, _syncServer);
            _peerManager = Substitute.For<IPeerManager>();
            _networkConfig = new NetworkConfig();
            _protocolValidator = new ProtocolValidator(_nodeStatsManager, _blockTree, forkInfo, _peerManager, _networkConfig, LimboLogs.Instance);
            _peerStorage = Substitute.For<INetworkStorage>();
            _syncPeerPool = Substitute.For<ISyncPeerPool>();
            _gossipPolicy = Substitute.For<IGossipPolicy>();
            _manager = new ProtocolsManager(
                _syncPeerPool,
                _syncServer,
                RunImmediatelyScheduler.Instance,
                _txPool,
                _pooledTxsRequestor,
                _discoveryApp,
                _serializer,
                _rlpxHost,
                _nodeStatsManager,
                _protocolValidator,
                _peerStorage,
                forkInfo,
                _gossipPolicy,
                Substitute.For<IWorldStateManager>(),
                LimboLogs.Instance);
        }

        public Context CreateIncomingSession()
        {
            IChannel channel = Substitute.For<IChannel>();
            _currentSession = new Session(_localPort, channel, NullDisconnectsAnalyzer.Instance, LimboLogs.Instance);
            _pipeline.Get<ZeroNettyP2PHandler>().Returns(new ZeroNettyP2PHandler(_currentSession, LimboLogs.Instance));
            _rlpxHost.SessionCreated += Raise.EventWith(new object(), new SessionEventArgs(_currentSession));
            return this;
        }

        public Context CreateOutgoingSession()
        {
            IChannel channel = Substitute.For<IChannel>();
            _currentSession = new Session(_localPort, new Node(TestItem.PublicKeyB, _remoteHost, _remotePort), channel, NullDisconnectsAnalyzer.Instance, LimboLogs.Instance);
            _pipeline.Get<ZeroNettyP2PHandler>().Returns(new ZeroNettyP2PHandler(_currentSession, LimboLogs.Instance));
            _rlpxHost.SessionCreated += Raise.EventWith(new object(), new SessionEventArgs(_currentSession));
            return this;
        }

        public Context Handshake()
        {
            _currentSession.Handshake(TestItem.PublicKeyB);
            return this;
        }

        public Context Init()
        {
            _currentSession.Init(5, _channelHandlerContext, _packetSender);
            return this;
        }

        public Context ActivateChannel()
        {
            _currentSession.RemoteHost = _remoteHost;
            _currentSession.RemotePort = _remotePort;
            return this;
        }

        public Context VerifyPingSenderSet()
        {
            Assert.That(_currentSession.PingSender, Is.Not.Null);
            return this;
        }

        public Context VerifyDisconnected()
        {
            Assert.That(_currentSession.State, Is.EqualTo(SessionState.Disconnected));
            return this;
        }

        public Context ReceiveDisconnect()
        {
            using DisconnectMessage message = new(EthDisconnectReason.Other);
            IByteBuffer disconnectPacket = _serializer.ZeroSerialize(message);

            // to account for AdaptivePacketType byte
            disconnectPacket.ReadByte();
            _currentSession.ReceiveMessage(new ZeroPacket(disconnectPacket) { PacketType = P2PMessageCode.Disconnect });
            return this;
        }

        public Context VerifyInitialized()
        {
            Assert.That(_currentSession.State, Is.EqualTo(SessionState.Initialized));
            return this;
        }

        public Context VerifyProtocolVersion(string protocol, int version)
        {
            Assert.That(_manager.GetHighestProtocolVersion(protocol), Is.EqualTo(version));
            return this;
        }

        public Context VerifyCompatibilityValidationType(CompatibilityValidationType expectedType)
        {
            Assert.That(_nodeStatsManager.GetOrAdd(_currentSession.Node).FailedCompatibilityValidation, Is.EqualTo(expectedType));
            return this;
        }

        public Context Disconnect()
        {
            _currentSession.MarkDisconnected(DisconnectReason.TooManyPeers, DisconnectType.Local, "test");
            return this;
        }

        public Context ReceiveStatus()
        {
            using StatusMessage msg = new();
            msg.TotalDifficulty = 1;
            msg.NetworkId = TestBlockchainIds.NetworkId;
            msg.GenesisHash = _blockTree.Genesis.Hash;
            msg.BestHash = _blockTree.Genesis.Hash;
            msg.ProtocolVersion = 66;
            msg.ForkId = new ForkId(0, 0);

            return ReceiveStatus(msg);
        }

        private Context ReceiveStatus(StatusMessage msg)
        {
            IByteBuffer statusPacket = _serializer.ZeroSerialize(msg);
            statusPacket.ReadByte();

            _currentSession.ReceiveMessage(new ZeroPacket(statusPacket) { PacketType = Eth62MessageCode.Status + 16 });
            return this;
        }

        public Context VerifyEthInitialized()
        {
            INodeStats stats = _nodeStatsManager.GetOrAdd(_currentSession.Node);
            Assert.That(stats.EthNodeDetails.NetworkId, Is.EqualTo(TestBlockchainIds.NetworkId));
            Assert.That(stats.EthNodeDetails.GenesisHash, Is.EqualTo(_blockTree.Genesis.Hash));
            Assert.That(stats.EthNodeDetails.ProtocolVersion, Is.EqualTo(66));
            Assert.That(stats.EthNodeDetails.TotalDifficulty, Is.EqualTo(BigInteger.One));
            return this;
        }

        public Context VerifySyncPeersRemoved()
        {
            _txPool.Received().RemovePeer(Arg.Any<PublicKey>());
            _syncPeerPool.Received().RemovePeer(Arg.Any<ISyncPeer>());
            return this;
        }

        private Context ReceiveHello(HelloMessage msg)
        {
            IByteBuffer helloPacket = _serializer.ZeroSerialize(msg);
            // to account for AdaptivePacketType byte
            helloPacket.ReadByte();

            _currentSession.ReceiveMessage(new ZeroPacket(helloPacket) { PacketType = P2PMessageCode.Hello });
            return this;
        }


        public Context ReceiveHello(byte p2pVersion = 5)
        {
            using HelloMessage msg = new();
            msg.Capabilities = new ArrayPoolList<Capability>(1) { new("eth", 66) };
            msg.NodeId = TestItem.PublicKeyB;
            msg.ClientId = "other client v1";
            msg.P2PVersion = p2pVersion;
            msg.ListenPort = 30314;

            return ReceiveHello(msg);
        }

        public Context ReceiveHelloNoEth()
        {
            using HelloMessage msg = new();
            msg.Capabilities = ArrayPoolList<Capability>.Empty();
            msg.NodeId = TestItem.PublicKeyB;
            msg.ClientId = "other client v1";
            msg.P2PVersion = 5;
            msg.ListenPort = 30314;
            return ReceiveHello(msg);
        }

        public Context ReceiveHelloEth(int protocolVersion)
        {
            using HelloMessage msg = new();
            msg.Capabilities = new ArrayPoolList<Capability>(1) { new("eth", protocolVersion) };
            msg.NodeId = TestItem.PublicKeyB;
            msg.ClientId = "other client v1";
            msg.P2PVersion = 5;
            msg.ListenPort = 30314;
            return ReceiveHello(msg);
        }


        public Context ReceiveHelloWrongEth()
        {
            return ReceiveHelloEth(65);
        }

        public Context ReceiveStatusWrongChain(ulong networkId)
        {
            using StatusMessage msg = new();
            msg.TotalDifficulty = 1;
            msg.NetworkId = networkId;
            msg.GenesisHash = TestItem.KeccakA;
            msg.BestHash = TestItem.KeccakA;
            msg.ProtocolVersion = 66;

            return ReceiveStatus(msg);
        }

        public Context ReceiveStatusWrongGenesis()
        {
            using StatusMessage msg = new();
            msg.TotalDifficulty = 1;
            msg.NetworkId = TestBlockchainIds.NetworkId;
            msg.GenesisHash = TestItem.KeccakB;
            msg.BestHash = TestItem.KeccakB;
            msg.ProtocolVersion = 66;

            return ReceiveStatus(msg);
        }
    }

    [Test]
    public void Sets_ping_sender_after_receiving_hello()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .ReceiveHello()
            .VerifyPingSenderSet();
    }

    [Test]
    public void Disconnects_on_p2p_before_version_4()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .ReceiveHello(3)
            .VerifyDisconnected();
    }

    [Test]
    public void Disconnects_on_receiving_disconnect()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .ReceiveHello()
            .ReceiveDisconnect()
            .VerifyDisconnected();
    }

    [Test]
    public void Runs_ok_when_initializing_protocol_on_a_closing_session()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .Disconnect()
            .ReceiveHello();
    }

    [Test]
    public void Can_initialize_a_session()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyInitialized();
    }

    [Test]
    public void Can_initialize_eth_protocol()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyInitialized()
            .ReceiveHello()
            .ReceiveStatus()
            .VerifyEthInitialized();
    }

    [Test]
    public void Removes_sync_peers_on_disconnect()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyInitialized()
            .ReceiveHello()
            .ReceiveStatus()
            .VerifyEthInitialized()
            .Disconnect()
            .VerifySyncPeersRemoved();
    }

    [Test]
    public void Disconnects_on_missing_eth()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyInitialized()
            .ReceiveHelloNoEth()
            .VerifyDisconnected();
    }

    [Test]
    public void Disconnects_on_wrong_eth()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyInitialized()
            .ReceiveHelloWrongEth()
            .VerifyDisconnected();
    }

    [TestCase(TestBlockchainIds.NetworkId + 1)]
    [TestCase(TestBlockchainIds.ChainId)]
    public void Disconnects_on_wrong_network_id(int networkId)
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyInitialized()
            .ReceiveHello()
            .ReceiveStatusWrongChain((ulong)networkId)
            .VerifyCompatibilityValidationType(CompatibilityValidationType.NetworkId)
            .VerifyDisconnected();
    }

    [Test]
    public void Disconnects_on_wrong_genesis_hash()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyInitialized()
            .ReceiveHello()
            .ReceiveStatusWrongGenesis()
            .VerifyDisconnected();
    }

    [Test]
    public void Initialized_with_eth66_only()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyInitialized()
            .ReceiveHelloEth(66)
            .VerifyInitialized();
    }

    [Test]
    public void Has_correct_highest_eth_protocol_version()
    {
        When
            .CreateIncomingSession()
            .ActivateChannel()
            .Handshake()
            .Init()
            .VerifyProtocolVersion(Protocol.Eth, 68);
    }
}

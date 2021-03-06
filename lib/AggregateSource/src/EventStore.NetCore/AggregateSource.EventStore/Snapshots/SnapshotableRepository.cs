﻿using System;
using System.Linq;

using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;

namespace AggregateSource.EventStore.Snapshots
{
    /// <summary>
    ///     Represents a virtual collection of <typeparamref name="TAggregateRoot" />.
    /// </summary>
    /// <typeparam name="TAggregateRoot">The type of the aggregate root in this collection.</typeparam>
    public class SnapshotableRepository<TAggregateRoot> : IRepository<TAggregateRoot>
        where TAggregateRoot : IAggregateRootEntity, ISnapshotable
    {
        private readonly EventReaderConfiguration _configuration;
        private readonly IEventStoreConnection _connection;
        private readonly ISnapshotReader _reader;
        private readonly Func<TAggregateRoot> _rootFactory;
        private readonly UnitOfWork _unitOfWork;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SnapshotableRepository{TAggregateRoot}" /> class.
        /// </summary>
        /// <param name="rootFactory">The aggregate root entity factory.</param>
        /// <param name="unitOfWork">The unit of work to interact with.</param>
        /// <param name="connection">The event store connection to use.</param>
        /// <param name="configuration">The event store configuration to use.</param>
        /// <param name="reader">The snapshot reader to use.</param>
        /// <exception cref="System.ArgumentNullException">
        ///     Thrown when the <paramref name="rootFactory" /> or
        ///     <paramref name="unitOfWork" /> or <paramref name="connection" /> or <paramref name="configuration" /> is null.
        /// </exception>
        public SnapshotableRepository(Func<TAggregateRoot> rootFactory, UnitOfWork unitOfWork,
            IEventStoreConnection connection, EventReaderConfiguration configuration,
            ISnapshotReader reader)
        {
            _rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        /// <summary>
        ///     Gets the aggregate root entity associated with the specified aggregate identifier.
        /// </summary>
        /// <param name="identifier">The aggregate identifier.</param>
        /// <returns>An instance of <typeparamref name="TAggregateRoot" />.</returns>
        /// <exception cref="AggregateNotFoundException">Thrown when an aggregate is not found.</exception>
        public TAggregateRoot Get(string identifier)
        {
            var result = GetOptional(identifier);
            if (!result.HasValue)
            {
                throw new AggregateNotFoundException(identifier, typeof(TAggregateRoot));
            }
            return result.Value;
        }

        /// <summary>
        ///     Attempts to get the aggregate root entity associated with the aggregate identifier.
        /// </summary>
        /// <param name="identifier">The aggregate identifier.</param>
        /// <returns>The found <typeparamref name="TAggregateRoot" />, or empty if not found.</returns>
        public Optional<TAggregateRoot> GetOptional(string identifier)
        {
            if (_unitOfWork.TryGet(identifier, out Aggregate aggregate))
            {
                return new Optional<TAggregateRoot>((TAggregateRoot)aggregate.Root);
            }
            Optional<Snapshot> snapshot = _reader.ReadOptional(identifier);
            var version = 1;
            if (snapshot.HasValue)
            {
                version = snapshot.Value.Version + 1;
            }
            UserCredentials streamUserCredentials = _configuration.StreamUserCredentialsResolver.Resolve(identifier);
            string streamName = _configuration.StreamNameResolver.Resolve(identifier);
            StreamEventsSlice slice = _connection.
                ReadStreamEventsForwardAsync(
                    streamName, version, _configuration.SliceSize, false, streamUserCredentials).
                Result;
            if (slice.Status == SliceReadStatus.StreamDeleted || slice.Status == SliceReadStatus.StreamNotFound)
            {
                return Optional<TAggregateRoot>.Empty;
            }

            TAggregateRoot root = _rootFactory();
            if (snapshot.HasValue)
            {
                root.RestoreSnapshot(snapshot.Value.State);
            }

            root.Initialize(slice.Events.Select(resolved => _configuration.Deserializer.Deserialize(resolved)));
            while (!slice.IsEndOfStream)
            {
                slice = _connection.
                    ReadStreamEventsForwardAsync(
                        streamName, slice.NextEventNumber, _configuration.SliceSize, false, streamUserCredentials).
                    Result;

                root.Initialize(slice.Events.Select(resolved => _configuration.Deserializer.Deserialize(resolved)));
            }
            aggregate = new Aggregate(identifier, (int)slice.LastEventNumber, root);
            _unitOfWork.Attach(aggregate);
            return new Optional<TAggregateRoot>(root);
        }

        /// <summary>
        ///     Adds the aggregate root entity to this collection using the specified aggregate identifier.
        /// </summary>
        /// <param name="identifier">The aggregate identifier.</param>
        /// <param name="root">The aggregate root entity.</param>
        public void Add(string identifier, TAggregateRoot root)
        {
            _unitOfWork.Attach(new Aggregate(identifier, (int)ExpectedVersion.NoStream, root));
        }
    }
}

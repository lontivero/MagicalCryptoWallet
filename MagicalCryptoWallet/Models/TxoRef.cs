﻿using MagicalCryptoWallet.Converters;
using MagicalCryptoWallet.Helpers;
using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace MagicalCryptoWallet.Models
{
	/// <summary>
	/// The same functionality as Outpoint, but it's JsonSerializable
	/// </summary>
	[JsonObject(MemberSerialization.OptIn)]
	public class TxoRef : IEquatable<TxoRef>, IEquatable<OutPoint>
	{
		[JsonProperty(Order = 1)]
		[JsonConverter(typeof(Uint256Converter))]
		public uint256 TransactionId { get; }

		[JsonProperty(Order = 2)]
		public int Index { get; }

		[JsonConstructor]
		public TxoRef(uint256 transactionId, int index)
		{
			TransactionId = Guard.NotNull(nameof(transactionId), transactionId);
			Index = Guard.NotNull(nameof(index), index);
		}

		public TxoRef(OutPoint outPoint)
		{
			Guard.NotNull(nameof(outPoint), outPoint);
			TransactionId = outPoint.Hash;
			Index = (int)outPoint.N;
		}

		public OutPoint ToOutPoint() => new OutPoint(TransactionId, Index);

		#region EqualityAndComparison

		public override bool Equals(object obj) => obj is TxoRef && this == (TxoRef)obj;
		public bool Equals(TxoRef other) => this == other;
		public override int GetHashCode() => TransactionId.GetHashCode() ^ Index;
		public static bool operator ==(TxoRef x, TxoRef y) => y?.TransactionId == x?.TransactionId && y?.Index == x?.Index;
		public static bool operator !=(TxoRef x, TxoRef y) => !(x == y);

		public bool Equals(OutPoint other) => TransactionId == other?.Hash && Index == other?.N;
		public static bool operator ==(OutPoint x, TxoRef y) => y?.TransactionId == x?.Hash && y?.Index == x?.N;
		public static bool operator ==(TxoRef x, OutPoint y) => y?.Hash == x?.TransactionId && y?.N == x?.Index;
		public static bool operator !=(OutPoint x, TxoRef y) => !(x == y);
		public static bool operator !=(TxoRef x, OutPoint y) => !(x == y);

		#endregion
	}
}

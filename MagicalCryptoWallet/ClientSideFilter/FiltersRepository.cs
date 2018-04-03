using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace MagicalCryptoWallet.Backend
{
	public class FilterRepository : IDisposable
	{
		private readonly FilterStore _store;
		private readonly CachedIndex _index;

		public FilterRepository(FilterStore store, CachedIndex index)
		{
			_store = store;
			_index = index;
		}

		public static FilterRepository Open(string folderPath)
		{
			var dataDirectory = new DirectoryInfo(folderPath);
			var filterStream = File.Open(Path.Combine(folderPath, "filters.dat"), FileMode.OpenOrCreate);
			var indexStream = File.Open(Path.Combine(folderPath,  "filters.idx"), FileMode.OpenOrCreate);
			var filterStore = new FilterStore(filterStream);
			var indexStore = new Index(indexStream);
			var fastIndexStore = new CachedIndex(indexStore);
			var repo = new FilterRepository(filterStore, fastIndexStore);
			return repo;
		}

		public IEnumerable<GolombRiceFilter> Get(uint256 key)
		{
			var offset = _index.Get(key);
			var filter = _store.GetFrom(offset).FirstOrDefault();
			if(filter == null || Hashes.Hash256(filter.Data.ToByteArray()) != key){
				return Enumerable.Empty<GolombRiceFilter>();
			}
			return _store.GetFrom(offset);
		}

		public IEnumerable<GolombRiceFilter> Get()
		{
			return _store.GetFrom(0);
		}

		public void Append(uint256 key, GolombRiceFilter filter)
		{
			var pos = _store.Append(filter);
			_index.Append(key, pos);
		}

		public void Delete(uint256 key)
		{
			var offset = _index.Get(key);
			var filter = _store.GetFrom(offset).First();
			var empty = new GolombRiceFilter(filter.Data, 0, 0);
			_store.Write(offset, empty);
		}

		#region IDisposable Support
		private bool _disposedValue = false; 

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				((IDisposable)_store)?.Dispose();
				((IDisposable)_index)?.Dispose();
				_disposedValue = true;
			}
		}

		~FilterRepository() {
		   Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}

	public class FilterStore : Store<GolombRiceFilter>
	{
		private const short MagicSeparatorNumber = 0x4691;

		public FilterStore(Stream stream) 
			: base(stream)
		{
		}

		protected override GolombRiceFilter Read(BinaryReader reader)
		{
			var magic = reader.ReadInt16();
			if (magic != MagicSeparatorNumber)
				return null;
			var entryCount = reader.ReadInt32();
			var bitArrayLen = reader.ReadInt32();
			var byteArrayLen = GetArrayLength(bitArrayLen, 8);
			var data = reader.ReadBytes(byteArrayLen);
			var bitArray = new FastBitArray(data);
			bitArray.Length = bitArrayLen;
			return new GolombRiceFilter (bitArray, entryCount);
		}

		protected override void Write(BinaryWriter writer, GolombRiceFilter filter)
		{
			var data = filter.Data.ToByteArray();

			writer.Write(MagicSeparatorNumber);
			writer.Write(filter.N);
			writer.Write(filter.Data.Length);
			writer.Write(data);
			writer.Flush();
		}

		public IEnumerable<GolombRiceFilter> GetFrom(int offset)
		{
			return Enumerate(offset).Where(x=>x.P > 0 && x.N > 0);
		}

		private static int GetArrayLength(int n, int div)
		{
			return n <= 0 ? 0 : (n - 1) / div + 1;
		}
	}
}

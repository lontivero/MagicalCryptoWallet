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
	public abstract class Store<T> : IEnumerable<T>, IDisposable
	{
		private readonly Stream _stream;

		protected Store(Stream stream)
		{
			_stream = stream;
		}

		public int Append(T item)
		{
			if (item == null)
				throw new ArgumentNullException(nameof(item));

			_stream.Seek(0, SeekOrigin.End);
			using (var bw = new BinaryWriter(_stream, new UTF8Encoding(false, false), true))
			{
				var pos = _stream.Position;
				Write(bw, item);
				return (int)pos;
			}
		}

		public void Write(int offset, T item)
		{
			if (item == null)
				throw new ArgumentNullException(nameof(item));

			_stream.Seek(offset, SeekOrigin.Begin);
			using (var bw = new BinaryWriter(_stream, new UTF8Encoding(false, false), true))
			{
				Write(bw, item);
			}
		}

		protected IEnumerable<T> Enumerate(int offset = 0)
		{
			_stream.Seek(offset, SeekOrigin.Begin);
			using (var br = new BinaryReader(_stream, new UTF8Encoding(false, false), true))
			{
				while (_stream.Position < _stream.Length)
				{
					yield return Read(br);
				}
			}
		}

		protected abstract T Read(BinaryReader reader);

		protected abstract void Write(BinaryWriter writer, T item);

		public IEnumerator<T> GetEnumerator()
		{
			return Enumerate().GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#region IDisposable Support
		private bool _disposedValue = false; 

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					_stream.Dispose();
				}

				_disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
		#endregion
	}



	public class Index : Store<IndexEntry>
	{
		public Index(Stream stream)
			: base(stream)
		{
		}

		public virtual int Get(uint256 key)
		{
			return this.Single(x=>x.Key == key).Offset;
		}

		public void Append(uint256 key, int offset)
		{
			Append(new IndexEntry(key, offset));
		}

		protected override IndexEntry Read(BinaryReader reader)
		{
			var key = reader.ReadBytes(32);
			var pos = reader.ReadInt32();
			return new IndexEntry(new uint256(key), pos);
		}

		protected override void Write(BinaryWriter writer, IndexEntry indexEntry)
		{
			writer.Write(indexEntry.Key.ToBytes(), 0, 32);
			writer.Write(indexEntry.Offset);
		}
	}

	public class CachedIndex: IDisposable
	{
		private readonly Index _index;
		private readonly Dictionary<uint256, int> _cache;

		public CachedIndex(Index index)
		{
			_index = index;
			_cache = new Dictionary<uint256, int>();
			Load();
		}

		private void Load()
		{
			foreach (var i in _index)
			{
				_cache.Add(i.Key, i.Offset);
			}
		}

		public int Get(uint256 key)
		{
			return _cache[key];
		}

		public void Append(uint256 key, int offset)
		{
			_index.Append(key, offset);
			_cache[key] = offset;
		}

		#region IDisposable Support
		private bool disposedValue = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
				}

				_index.Dispose();
				_cache.Clear();

				disposedValue = true;
			}
		}

		~CachedIndex() {
		   Dispose(false);
		}

		void IDisposable.Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}

	public class IndexEntry
	{
		public uint256 Key { get; }
		public int Offset { get; }

		public IndexEntry(uint256 key, int offset)
		{
			Key = key;
			Offset = offset;
		}
	}
}

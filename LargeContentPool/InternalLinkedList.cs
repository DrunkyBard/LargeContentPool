using System;
using System.Collections;
using System.Collections.Generic;

namespace LargeContentPool
{
	internal sealed class InternalLinkedList<T> : IEnumerable<T>
	{
		private Node _first;
		private Node _last;

		public int Count { get; private set; }

		public void AddFirst(T value)
		{
			_first = new Node(null, _first, value);

			if (_last == null)
			{
				_last = _first;
			}

			Count++;
		}

		public void AddLast(T value)
		{
			if (_first == null)
			{
				_first = new Node(null, null, value);
				_last = _first;
				Count++;

				return;
			}

			_last.Next = new Node(_last, null, value);
			_last = _last.Next;

			Count++;
		}

		public Node GetFirst()
		{
			if (_first == null)
			{
				throw new InvalidOperationException("Linked list is empty");
			}

			return _first;
		}

		public Node GetLast()
		{
			if (_last == null)
			{
				throw new InvalidOperationException("Linked list is empty");
			}

			return _last;
		}

		public void RemoveFirst()
		{
			if (_first == null)
			{
				return;
			}

			_first = _first.Next;

			if (_first == null)
			{
				_last = null;
			}

			Count--;
		}

		public void RemoveLast()
		{
			if (_last == null)
			{
				return;
			}

			_last = _last.Next;

			if (_last == null)
			{
				_first = null;
			}

			Count--;
		}

		public sealed class Node
		{
			public Node Next;
			public Node Previous;
			public T Value;

			public Node(Node previous, Node next, T value)
			{
				Previous = previous;
				Next = next;
				Value = value;
			}

			public void Remove()
			{
				Previous.Next = Next;
				Next.Previous = Previous;
			}
		}

		public Enumerator GetEnumerator() => new Enumerator(_first);

		IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(_first);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public sealed class Enumerator : IEnumerator<T>
		{
			private Node _node;
			private T _current;

			internal Enumerator(Node node)
			{
				_node = node;
			}

			public void Dispose()
			{ }

			public bool MoveNext()
			{
				if (_node == null)
				{
					return false;
				}

				_current = _node.Value;
				_node = _node.Next;

				return true;
			}

			public void Reset()
			{ }

			public T Current => _current;

			object IEnumerator.Current => Current;
		}
	}
}

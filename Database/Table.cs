using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Database.Commands;
using Utilities;

namespace Database {
	public class Table<T1, T2> : BaseTable, IEnumerable<T2> where T2 : Tuple {
		public bool UseUniqueId { get; private set; }
		public virtual CommandsHolder<T1, T2> Commands { get; private set; }
		private int _uid = 1;

		public int GenerateUniqueId() {
			return _uid++;
		}

		public void ResetUniqueId() {
			_uid = 1;
		}

		private readonly Dictionary<T1, T2> _tuples = new Dictionary<T1, T2>();
		internal TkDictionary<int, int> AutoIncrements = new TkDictionary<int, int>();
		public bool EnableRawEvents { get; set; }

		public Dictionary<T1, T2> Tuples {
			get { return _tuples; }
		}

		public int Cardinality {
			get { return _tuples.Count; }
		}
		public int Count {
			get { return Cardinality; }
		}

		public event TableEventHandler TupleRemoved;
		public event TableEventHandler TupleAdded;
		public event TableEventHandler TupleRawAdded;
		public event TableEventHandler TupleModified;
		public event UpdateTableEventHandler TableUpdated;

		public delegate void TableEventHandler(object sender, T1 key, T2 value);
		public delegate void UpdateTableEventHandler(object sender);

		public void OnTableUpdated() {
			UpdateTableEventHandler handler = TableUpdated;
			if (handler != null) handler(this);
		}

		public virtual void OnTupleRawAdded(T1 key, T2 value) {
			TableEventHandler handler = TupleRawAdded;
			if (handler != null) handler(this, key, value);
		}

		public virtual void OnTupleModified(T1 key, T2 value) {
			if (!EnableEvents) return;
			TableEventHandler handler = TupleModified;
			if (handler != null) handler(this, key, value);
		}

		public virtual void OnTupleAdded(T1 key, T2 value) {
			if (!EnableEvents) return;
			TableEventHandler handler = TupleAdded;
			if (handler != null) handler(this, key, value);
		}

		public virtual void OnTupleRemoved(T1 key, T2 value) {
			if (!EnableEvents) return;
			TableEventHandler handler = TupleRemoved;
			if (handler != null) handler(this, key, value);
		}

		public Table(AttributeList list, bool useUniqueId) {
			if (typeof (T1) != list.PrimaryAttribute.DataType)
				throw new Exception("The primary attribute type doesn't match the database primary key");

			UseUniqueId = useUniqueId;
			Commands = new CommandsHolder<T1, T2>(this);
			_list = list;
		}

		public object this[T1 key, object input] {
			get {
				DatabaseExceptions.ThrowIfTraceNotEnabled();

				int index = _list.TryFind(input);

				if (index < 0) {
					if (input is string inputString) {
						// Attempt to find value on model
						var model = Get(key, DbAttribute.DefaultModel);
						return TableHelper.GetValue((int)(object)key, this, model, inputString);
					}

					throw DatabaseExceptions.CreateAttributeNotFound(input, _list);
				}
				else {
					var attribute = _list.Attributes[index];

					if (attribute.IsModelAttribute) {
						var model = Get(key, attribute);
						TableHelper.TrackModel((int)(object)key, this, model);
						return model;
					}

					return Get(key, attribute);
				}
			}
			set {
				DatabaseExceptions.ThrowIfTraceNotEnabled();

				if (!ContainsKey(key)) {
					T2 element = Compiled.New2<T2>.Instance();
					element.Init(key, _list);
					element.Added = true;

					Commands.AddTuple(key, element);
				}

				int index = _list.TryFind(input);

				if (index < 0) {
					if (input is string inputString) {
						// Attempt to find value on model
						var model = Get(key, DbAttribute.DefaultModel);
						var result = TypeTreeHelper.GetValue(model, inputString);

						if (result == null || result.Count == 0)
							throw DatabaseExceptions.CreateModelFieldNotFoundException(input, model.GetType());

						TableHelper.TrackModel((int)(object)key, this, model);
						var value2 = result.First();

						if (value2 is string valueString) {
							TypeTreeHelper.SetValue(model, inputString, value.ToString());
							return;
						}
						else if (value2 is Enum valueEnum) {
							if (valueEnum.GetType().GetEnumUnderlyingType() == typeof(Int64)) {
								TypeTreeHelper.SetValue(model, inputString, Int64.Parse(value.ToString()));
								return;
							}
							else if (valueEnum.GetType().GetEnumUnderlyingType() == typeof(Int32)) {
								TypeTreeHelper.SetValue(model, inputString, Int32.Parse(value.ToString()));
								return;
							}
						}

						TypeTreeHelper.SetValue(model, inputString, value.ToString());
						return;
					}

					throw DatabaseExceptions.CreateAttributeNotFound(input, _list);
				}

				if (index == 0) {
					if (!(value is T1))
						DatabaseExceptions.ThrowKeyConstraint<T1>(value);

					T1 newKey = (T1)value;

					if (newKey.ToString() == key.ToString())
						return;

					Commands.ChangeKey(key, newKey);
					return;
				}
				else if (_list[index].IsModelAttribute) {
					throw new Exception("Cannot replace a model attribute directly.");
				}

				Commands.Set(_tuples[key], index, value);
			}
		}

		public object this[T1 key] {
			get {
				DatabaseExceptions.ThrowIfTraceNotEnabled();
				var model = Get(key, DbAttribute.DefaultModel);
				TableHelper.TrackModel((int)(object)key, this, model);
				return model;
			}
			set {
				DatabaseExceptions.ThrowIfTraceNotEnabled();

				if (value == null) {
					this.Commands.Delete(key);
					return;
				}

				if (value is T2 tuple) {
					T2 element = Compiled.New2<T2>.Instance();
					element.Init(key, _list);
					element.Added = true;
					element.Copy(tuple);
					element.SetRawValue(0, key);

					Commands.AddTuple(key, element);
				}
				else if (value is ICloneable cloneable) {
					T2 element = Compiled.New2<T2>.Instance();
					element.Init(key, _list);
					element.Added = true;
					element.SetRawValue(DbAttribute.DefaultModel, cloneable.Clone());

					Commands.AddTuple(key, element);
				}
				else {
					throw new Exception("Invalid value assigned to table. Expected a model or a tuple.");
				}
			}
		}

		public virtual List<T2> FastItems {
			get {
				return _tuples.Select(p => p.Value).ToList();
			}
		}

		public virtual object Get(T1 key, DbAttribute attribute) {
			return _tuples[key].GetValue(attribute.Index);
		}

		public virtual void Set(T1 key, DbAttribute attribute, object value) {
			_tuples[key].SetValue(attribute, value);
		}

		public virtual T Get<T>(T1 key, DbAttribute attribute) where T : class {
			return _tuples[key].GetValue(attribute.Index) as T;
		}

		public virtual object GetRaw(T1 key, DbAttribute attribute) {
			return _tuples[key].GetRawValue(attribute.Index);
		}

		public virtual void SetRaw(T1 key, DbAttribute attribute, object value, bool forceSet = false) {
			// Always add inexisting tuples automatically
			if (EnableRawEvents) {
				bool storeAddCommand = false;
				T2 tuple = null;

				if (!_tuples.TryGetValue(key, out tuple)) {
					tuple = Compiled.New2<T2>.Instance();
					tuple.Init(key, _list);
					_tuples.Add(key, tuple);

					if (EnableRawEvents) {
						tuple.Added = true;
						storeAddCommand = true;
					}
				}

				// Send the add tuple last, because models would be null otherwise
				Commands.StoreAndExecute(new ChangeTupleProperties<T1, T2>(tuple, attribute, value));

				if (storeAddCommand)
					Commands.StoreAndExecute(new AddTuple<T1, T2>(key, tuple, null) { IgnoreConflict = true });
			}
			else {
				EnsureExists(key).SetRawValue(attribute, value);
			}
		}

		public virtual void SetRawRange(T1 key, int attributeOffset, int indexOffset, List<DbAttribute> attributes, string[] values) {
			object[] valuesObject = new object[values.Length];

			for (int i = indexOffset; i < values.Length; i++)
				valuesObject[i] = values[i];

			SetRawRange(key, attributeOffset, indexOffset, attributes, valuesObject);
		}

		public virtual void SetRawRange(T1 key, int attributeOffset, int indexOffset, List<DbAttribute> attributes, object[] values) {
			if (values.Length == 1) {
				SetRaw(key, attributes[attributeOffset], values[0]);
				return;
			}

			if (!_tuples.ContainsKey(key)) {
				T2 element = Compiled.New2<T2>.Instance();
				element.Init(key, _list);
				_tuples.Add(key, element);

				if (EnableRawEvents) {
					T2 tuple = _tuples[key];
					tuple.Added = true;

					for (int i = indexOffset; i < values.Length && i + attributeOffset < attributes.Count; i++) {
						tuple.SetRawValue(attributes[i + attributeOffset], values[i]);
					}

					Commands.StoreAndExecute(new AddTuple<T1, T2>(key, tuple, null) {IgnoreConflict = true});
					return;
				}
			}

			if (EnableRawEvents) {
				Commands.StoreAndExecute(new ChangeTuplePropertyRange<T1, T2>(key, _tuples[key], attributeOffset, indexOffset, attributes, values));
			}
			else {
				var tuple = _tuples[key];

				for (int i = indexOffset; i < values.Length && i + attributeOffset < attributes.Count; i++) {
					tuple.SetRawValue(attributes[i + attributeOffset], values[i]);
				}
			}
		}

		public virtual void Clear() {
			_tuples.Clear();
		}

		public virtual bool ContainsKey(T1 key) {
			return _tuples.ContainsKey(key);
		}
		public virtual void ChangeKey(T1 oldKey, T1 newKey) {
			if (_tuples.ContainsKey(oldKey)) {
				T2 temp = _tuples[oldKey];
				_tuples.Remove(oldKey);
				_tuples[newKey] = temp;
			}
		}
		public virtual void Remove(T1 key) {
			if (_tuples.ContainsKey(key)) {
				T2 tuple = _tuples[key];
				_tuples.Remove(key);
				OnTupleRemoved(key, tuple);
			}
		}
		public virtual void Add(T1 key, T2 item) {
			_tuples[key] = item;
			OnTupleAdded(key, item);
		}

		public virtual IEnumerable<T2> GetSortedItems() {
			return _tuples.OrderBy(p => p.Key).Select(p => p.Value);
		}

		public virtual T2 Copy(T1 elementFromId, T1 elementToId) {
			T2 elementFrom = _tuples[elementFromId];

			T2 elementTo = Utilities.Compiled.New2<T2>.Instance();
			elementTo.Init(elementToId, _list);

			//T2 elementTo = (T2) Activator.CreateInstance(typeof (T2), elementToId, _list);

			foreach (DbAttribute attribute in _list.Attributes) {
				if (!typeof(IBinding).IsAssignableFrom(attribute.DataType)) {
					var copy = elementFrom.GetRawCopyValue(attribute.Index);

					if (copy is IModel model)
						model.SetKey(elementToId);

					elementTo.SetRawValue(attribute, copy);
				}
			}

			elementTo.SetRawValue(_list.PrimaryAttribute, elementToId);
			Remove(elementToId);
			Add(elementToId, elementTo);
			return elementTo;
		}

		public virtual T2 Copy(T1 elementFromId) {
			T2 elementFrom = _tuples[elementFromId];

			T2 elementTo = Utilities.Compiled.New2<T2>.Instance();
			elementTo.Init(elementFromId, _list);

			//T2 elementTo = (T2)Activator.CreateInstance(typeof(T2), elementFromId, _list);

			foreach (DbAttribute attribute in _list.Attributes) {
				if (!typeof(IBinding).IsAssignableFrom(attribute.DataType)) {
					var copy = elementFrom.GetRawCopyValue(attribute.Index);

					if (copy is IModel model)
						model.SetKey(elementFromId);

					elementTo.SetRawValue(attribute, copy);
				}
			}

			elementTo.SetRawValue(_list.PrimaryAttribute, elementFromId);
			return elementTo;
		}

		public virtual T2 Copy(Table<T1, T2> source, T1 elementFromId, T1 elementToId) {
			T2 elementFrom = source._tuples[elementFromId];

			T2 elementTo = Utilities.Compiled.New2<T2>.Instance();
			elementTo.Init(elementToId, _list);

			foreach (DbAttribute attribute in _list.Attributes) {
				if (!typeof(IBinding).IsAssignableFrom(attribute.DataType)) {
					var copy = elementFrom.GetRawCopyValue(attribute.Index);
					
					if (copy is IModel model)
						model.SetKey(elementTo);

					elementTo.SetRawValue(attribute, copy);
				}
			}

			elementTo.SetRawValue(_list.PrimaryAttribute, elementToId);
			Remove(elementToId);
			Add(elementToId, elementTo);
			return elementTo;
		}

		public virtual T2 GetTuple(T1 key) {
			return _tuples[key];
		}

		public virtual T2 TryGetTuple(T1 key) {
			if (_tuples.ContainsKey(key))
				return _tuples[key];

			return null;
		}

		public override void ClearTupleStates() {
			foreach (T2 tuple in _tuples.Values.Where(p => !p.Normal)) {
				tuple.Modified = false;
				tuple.Added = false;
			}
		}

		internal override bool Contains(Tuple tuple) {
			T2 t = tuple as T2;
			if (t == null) return false;
			return _tuples.ContainsValue(t);
		}

		internal override void CommandSet(Tuple tuple, DbAttribute attribute, object value) {
			this[tuple.GetKey<T1>(), attribute.Index] = value;
		}

		internal override void CommandSetModel(Tuple tuple, object modelOriginal, object modelCurrent) {
			tuple.SetRawValue(1, modelOriginal);
			Commands.Set(tuple as T2, AttributeList[1], modelCurrent, false);
		}

		internal override bool TryFindModel(object model, out Tuple tuple) {
			foreach (var entry in _tuples) {
				if (entry.Value.GetModel() == model) {
					tuple = entry.Value;
					return true;
				}
			}

			tuple = null;
			return false;
		}

		internal override Tuple TryFindTuple(object key) {
			_tuples.TryGetValue((T1)key, out T2 value);
			return value;
		}

		public IEnumerator<T2> GetEnumerator() {
			return _tuples.Select(p => p.Value).OrderBy(p => p).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return GetEnumerator();
		}

		public void Add(T1 key) {
			DatabaseExceptions.ThrowIfTraceNotEnabled();
			
			T2 element = Compiled.New2<T2>.Instance();
			element.Init(key, _list);
			element.Added = true;

			Add(key, element);
		}

		public void Delete(T1 key) {
			DatabaseExceptions.ThrowIfTraceNotEnabled();
			Commands.Delete(key);
		}

		public T2 EnsureExists(T1 key) {
			if (!_tuples.TryGetValue(key, out var element)) {
				element = Compiled.New2<T2>.Instance();
				element.Init(key, _list);
				_tuples.Add(key, element);

				if (EnableRawEvents) {
					element.Added = true;
					Commands.StoreAndExecute(new AddTuple<T1, T2>(key, element, null) { IgnoreConflict = true });
				}
			}

			return element;
		}
	}
}

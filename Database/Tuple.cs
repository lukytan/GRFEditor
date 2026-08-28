using System;
using System.Collections.Generic;
using System.Linq;
using Utilities;

namespace Database {
	public class Tuple : IComparable {
		protected object[] _elements;
		private bool _modified;

		public bool Added { get; set; }
		public bool Deleted { get; set; }
		public bool Normal { get { return !(Added || Modified || Deleted); } }

		//public bool IsRedo { get; internal set; }
		//public bool IsUndo { get; internal set; }

		public AttributeList Attributes { get; private set; }

		public delegate void TupleEventHandler(object sender, bool value);

		public event TupleEventHandler TupleModified;

		public virtual void OnTupleModified(bool value) {
			TupleEventHandler handler = TupleModified;
			if (handler != null) handler(this, value);
		}

		public static Type BindingType = typeof (IBinding);

		public Tuple() {
		}

		public void Init(object key, AttributeList list) {
			Attributes = list;
			_elements = new object[list.Attributes.Count];

			_elements[0] = key;

			for (int index = 1; index < list.Attributes.Count; index++) {
				var attribute = list.Attributes[index];

				// This is not a real attribute
				if (BindingType.IsAssignableFrom(attribute.DataType)) {
					IBinding binding = (IBinding)Activator.CreateInstance(attribute.DataType, new object[] { });
					binding.Tuple = this;
					binding.AttachedAttribute = attribute;
					_elements[index] = binding;
				}
				else if (attribute.IsModelAttribute) {
					_elements[index] = Activator.CreateInstance(attribute.DataType);
				}
				else {
					_elements[index] = attribute.Default;
				}
			}
		}

		public Tuple(object key, AttributeList list) {
			Init(key, list);
		}

		public virtual bool Default {
			get {
				return true;
			}
		}

		public bool Modified {
			get { return _modified; }
			set {
				_modified = value;
				OnTupleModified(value);
			}
		}

		public virtual T GetKey<T>() {
			return (T) _elements[0];
		}

		public virtual object this[int index] {
			get {
				if (Attributes[index].IsModelAttribute) {
					var model = GetModel();
					TableHelper.TrackModel(-1, null, model);
					return model;
				}

				DbAttribute attribute = Attributes.Attributes[index];

				if (attribute.DataType == typeof(int)) {
					return attribute.DataConverter.ConvertFrom<int>(this, GetValue(index));
				}

				if (attribute.DataType == typeof(string)) {
					return attribute.DataConverter.ConvertFrom<string>(this, GetValue(index));
				}

				return attribute.Accessor.Get(this, GetValue(index));
			}
			set {
				DatabaseExceptions.ThrowIfTraceNotEnabled();
				DbAttribute attribute = Attributes.Attributes[index];

				foreach (var table in TableHelper.Tables) {
					if (table.Contains(this)) {
						table.CommandSet(this, attribute, value);
						break;
					}
				}
			}
		}

		public virtual object this[string input] {
			get {
				DatabaseExceptions.ThrowIfTraceNotEnabled();

				int index = Attributes.TryFind(input);

				if (index < 0) {
					// Attempt to find value on model
					var model = GetModel();
					return TableHelper.GetValue(-1, null, model, input);
				}

				var attribute = Attributes[index];

				if (attribute.IsModelAttribute) {
					var model = GetModel();
					TableHelper.TrackModel(-1, null, model);
					return model;
				}

				return this[index];
			}
			set {
				DatabaseExceptions.ThrowIfTraceNotEnabled();

				int index = Attributes.TryFind(input);

				if (index < 0) {
					// Attempt to find value on model
					var model = GetModel();
					var result = TypeTreeHelper.GetValue(model, input);

					if (result == null || result.Count == 0)
						throw DatabaseExceptions.CreateModelFieldNotFoundException(input, model.GetType());

					TableHelper.TrackModel(-1, null, model);
					var value2 = result.First();

					if (value2 is string valueString) {
						TypeTreeHelper.SetValue(model, input, value.ToString());
						return;
					}
					else if (value2 is Enum valueEnum) {
						if (valueEnum.GetType().GetEnumUnderlyingType() == typeof(Int64)) {
							TypeTreeHelper.SetValue(model, input, Int64.Parse(value.ToString()));
							return;
						}
						else if (valueEnum.GetType().GetEnumUnderlyingType() == typeof(Int32)) {
							TypeTreeHelper.SetValue(model, input, Int32.Parse(value.ToString()));
							return;
						}
					}

					TypeTreeHelper.SetValue(model, input, value.ToString());
					return;
				}

				if (Attributes[index].IsModelAttribute) {
					throw new Exception("Cannot replace a model attribute directly.");
				}

				this[index] = value;
			}
		}

		public TModel GetModel<TModel>() where TModel : class {
			return _elements[1] as TModel;
		}
		public object GetModel() {
			return _elements[1];
		}
		public virtual T GetValue<T>(int index) {
			return Attributes.Attributes[index].DataConverter.ConvertFrom<T>(this, GetValue(index));
		}
		public virtual T GetValue<T>(DbAttribute attribute) {
			return attribute.DataConverter.ConvertFrom<T>(this, GetValue(attribute.Index));
		}
		public virtual object GetValue(int index) {
			return _elements[index];
		}
		public virtual object GetValue(DbAttribute attribute) {
			return GetValue(attribute.Index);
		}
		public virtual void SetValue(DbAttribute attribute, object value) {
			_elements[attribute.Index] = attribute.DataConverter.ConvertTo(this, value);
		}
		public object GetRawCopyValue(int index) {
			return Attributes.Attributes[index].DataCopy.CopyFrom(_elements[index]);
		}
		public object GetRawValue(int index) {
			return _elements[index];
		}
		public T GetRawValue<T>(int index) {
			return (T) _elements[index];
		}
		public T GetRawValue<T>(DbAttribute attribute) {
			return (T)_elements[attribute.Index];
		}
		public void SetRawValue(DbAttribute attribute, object value) {
			_elements[attribute.Index] = value;
		}
		public void SetElements(object[] elements) {
			_elements = elements;
		}
		public void SetRawValue(int index, object value) {
			_elements[index] = value;
		}

		public object DataImage {
			get {
				if (GetImageData != null) {
					return GetImageData(this);
				}

				return null;
			}
		}

		public List<object> GetRawElements() {
			return _elements.ToList();
		}

		public bool CompareWith(Tuple tuple) {
			if (this._elements.Length != tuple._elements.Length)
				return false;

			if (this.Attributes.Attributes.Count != tuple.Attributes.Attributes.Count)
				return false;

			for (int i = 0; i < _elements.Length && i < tuple.Attributes.Attributes.Count; i++) {
				DbAttribute attribute = tuple.Attributes.Attributes[i];

				if (String.CompareOrdinal(GetValue<string>(attribute), tuple.GetValue<string>(attribute)) != 0)
					return false;
			}

			return true;
		}

		public Func<Tuple, object> GetImageData { get; set; }

		internal int GetHash() {
			return String.Join(",", _elements.Select(p => (p ?? "").ToString()).ToArray()).GetHashCode();
		}

		public int CompareTo(object obj) {
			if (obj == null) return 1;

			Tuple tuple = obj as Tuple;
			if (tuple == null)
				throw new ArgumentException("Object is not a Tuple");

			var o = tuple.GetValue(0);

			if (o is int)
				return ((int) this.GetValue(0)).CompareTo(o);
			return ((string)this.GetValue(0)).CompareTo(o);
		}

		public void Copy(Tuple tuple) {
			_elements = new object[tuple._elements.Length];

			for (int i = 0; i < tuple._elements.Length; i++) {
				if (BindingType.IsAssignableFrom(Attributes[i].DataType)) {
					IBinding binding = (IBinding)Activator.CreateInstance(Attributes[i].DataType, new object[] { });
					binding.Tuple = this;
					binding.AttachedAttribute = Attributes[i];
					_elements[i] = binding;
				}
				else {
					_elements[i] = tuple.GetRawCopyValue(i);
				}
			}
		}
	}
}

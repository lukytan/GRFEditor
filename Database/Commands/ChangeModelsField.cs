using System;
using System.Collections.Generic;
using System.Linq;
using Utilities;
using Utilities.Commands;

namespace Database.Commands {
	public class ChangeModelsField<TKey, TValue, TModel, TFieldValue> : ITableCommand<TKey, TValue>, ICombinableCommand where TValue : Tuple where TModel : new() {
		public Type ModelType;
		private Func<object, TFieldValue> _getter;
		private Action<object, TFieldValue> _setter;

		public TFieldValue[] OldValues;
		public TFieldValue[] NewValues;
		public object[] BaseModels;
		public TModel[] Models;
		private bool[] _isModified;
		public TKey[] Keys;
		private List<TValue> _tuples;
		private int[] _listEntriesAdded;
		private string _fieldName;
		private Func<TModel, TFieldValue> _newValueGetter;
		private Func<object, List<TModel>> _modelListGetter;
		private int _listIndex;
		private bool _isSet = false;
		private bool _oldValuesSet = false;
		private bool _newValuesSet = false;

		public ChangeModelsField(List<TValue> tuples, string fieldName, TFieldValue newValue) : this(tuples, fieldName, newValue, null, -1, null) {
		}

		public ChangeModelsField(List<TValue> tuples, string fieldName, TFieldValue newValue, Func<object, List<TModel>> modelListGetter, int listIndex, Func<TModel, TFieldValue> newValueGetter = null) {
			ModelType = typeof(TModel);

			_getter = ReflectionOptimizer<TFieldValue>.GetGetter(ModelType, fieldName);
			_setter = ReflectionOptimizer<TFieldValue>.GetSetter(ModelType, fieldName);
			_tuples = tuples;
			_fieldName = fieldName;
			_newValueGetter = newValueGetter;
			_modelListGetter = modelListGetter;
			_listIndex = listIndex;

			OldValues = new TFieldValue[tuples.Count];
			NewValues = new TFieldValue[tuples.Count];
			BaseModels = new object[tuples.Count];
			Models = new TModel[tuples.Count];
			_isModified = new bool[tuples.Count];
			Keys = new TKey[tuples.Count];
			_listEntriesAdded = new int[tuples.Count];

			for (int index = 0; index < tuples.Count; index++) {
				_listEntriesAdded[index] = 0;
				BaseModels[index] = tuples[index].GetModel();

				if (newValueGetter == null) {
					NewValues[index] = newValue;
				}

				Keys[index] = tuples[index].GetKey<TKey>();
				_isModified[index] = tuples[index].Modified;
			}

			if (newValueGetter == null)
				_newValuesSet = true;

			_trySetOldValues(true);
			Key = tuples[0].GetKey<TKey>();
		}

		private void _trySetOldValues(bool constructorCall) {
			if (_oldValuesSet)
				return;

			if (_modelListGetter != null && constructorCall) {
				for (int index = 0; index < _tuples.Count; index++) {
					var tuple = _tuples[index];
					var list = _modelListGetter(BaseModels[index]);

					if (_listIndex >= list.Count)
						return;
				}
			}

			for (int index = 0; index < _tuples.Count; index++) {
				var tuple = _tuples[index];

				if (_modelListGetter != null) {
					var list = _modelListGetter(BaseModels[index]);
					int count = 0;

					while (list.Count <= _listIndex) {
						list.Add(new TModel());
						count++;
					}

					_listEntriesAdded[index] = count;
					Models[index] = list[_listIndex];

					if (_newValueGetter != null && !_newValuesSet) {
						NewValues[index] = _newValueGetter(Models[index]);
					}

					OldValues[index] = _getter(Models[index]);
				}
				else {
					Models[index] = (TModel)BaseModels[index];

					if (_newValueGetter != null && !_newValuesSet) {
						NewValues[index] = _newValueGetter(Models[index]);
					}

					OldValues[index] = _getter(Models[index]);
				}
			}

			_newValuesSet = true;
			_oldValuesSet = true;
		}

		private string _valueToString(TFieldValue value) => value == null ? "" : value.ToString();

		public string CommandDescription => string.Format("[{0}...{1}], change '{2}' with '{3}'", _tuples.First().GetKey<TKey>(), _tuples.Last().GetKey<TKey>(), _fieldName, _valueToString(NewValues[0]).Replace("\r\n", "\\r\\n").Replace("\n", "\\n"));

		public TKey Key { get; private set; }
		public TValue Tuple => null;

		public void Execute(Table<TKey, TValue> table) {
			if (!_isSet) {
				_trySetOldValues(false);
				_isSet = true;
			}
			else if (_modelListGetter != null) {
				_oldValuesSet = false;
				_trySetOldValues(false);
			}

			for (int index = 0; index < _tuples.Count; index++) {
				bool oldModified = _isModified[index];
				bool newModified = oldModified;

				if (!newModified) {
					var oldValue = OldValues[index];
					var newValue = NewValues[index];

					if (oldValue == null || newValue == null)
						newModified = !ReferenceEquals(oldValue, newValue);
					else
						newModified = !oldValue.Equals(newValue);
				}

				_setter(Models[index], NewValues[index]);

				if (oldModified != newModified)
					_tuples[index].Modified = newModified;
			}
		}

		public void Undo(Table<TKey, TValue> table) {
			for (int index = 0; index < _tuples.Count; index++) {
				_setter(Models[index], OldValues[index]);
				_tuples[index].Modified = _isModified[index];

				if (_modelListGetter != null) {
					var list = _modelListGetter(BaseModels[index]);
					int count = _listEntriesAdded[index];

					if (count > 0) {
						list.RemoveRange(list.Count - count, count);
					}
				}
			}
		}

		public bool CanCombine(ICombinableCommand command) {
			if (command is ChangeModelsField<TKey, TValue, TModel, TFieldValue> cmd) {
				if (Keys.Length == cmd.Keys.Length && _fieldName == cmd._fieldName && ModelType == cmd.ModelType && _isSet && cmd._oldValuesSet) {
					for (int i = 0; i < Keys.Length; i++) {
						// type
						if (Keys[i] is int && cmd.Keys[i] is int) {
							if ((int)(object)Keys[i] != (int)(object)cmd.Keys[i]) {
								return false;
							}
						}
						else if (Keys[i] is string && cmd.Keys[i] is string) {
							if ((string)(object)Keys[i] != (string)(object)cmd.Keys[i]) {
								return false;
							}
						}
						else {
							return false;
						}

						if (_valueToString(NewValues[i]) != _valueToString(cmd.OldValues[i])) {
							return false;
						}
					}

					return true;
				}
			}

			return false;
		}

		public void Combine<T>(ICombinableCommand command, AbstractCommand<T> abstractCommand) {
			if (command is ChangeModelsField<TKey, TValue, TModel, TFieldValue> cmd) {
				for (int i = 0; i < Keys.Length; i++) {
					NewValues[i] = cmd.NewValues[i];
				}

				abstractCommand.ExplicitCommandExecution((T)(object)this);
			}
		}
	}
}

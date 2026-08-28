using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Utilities;

namespace Database {
	public static class TableHelper {
		public delegate void TupleTraceEventHandler(object sender, bool state);

		public static event TupleTraceEventHandler TraceStatusChanged;

		public static void OnTraceStatusChanged(bool state) {
			TupleTraceEventHandler handler = TraceStatusChanged;
			if (handler != null) handler(null, state);
		}

		public struct ModelTracker {
			public int Key;
			public BaseTable Table;
			public object Model;
		}

		private static Dictionary<object, ModelTracker> _trackedModels = new Dictionary<object, ModelTracker>();

		public static void TrackModel(int key, BaseTable table, object model) {
			if (model is ICloneable clone) {
				if (!_trackedModels.ContainsKey(model)) {
					_trackedModels[model] = new ModelTracker() { Model = clone.Clone(), Key = key, Table = table };
				}
			}
		}

		public static bool EnableTupleTrace {
			get { return _enableTupleTrace; }
			set {
				_enableTupleTrace = value;

				// Apply changes
				if (value == false) {
					foreach (var entry in _trackedModels) {
						ApplyTrackedChanges(entry);
					}
				}

				_trackedModels.Clear();

				OnTraceStatusChanged(_enableTupleTrace);
			}
		}

		private static void ApplyTrackedChanges(KeyValuePair<object, ModelTracker> entry) {
			var tracker = entry.Value;
			var original = tracker.Model;
			var current = entry.Key;

			if (!original.Equals(current)) {
				if (tracker.Table != null && tracker.Key > -1) {
					var tuple = tracker.Table.TryFindTuple(tracker.Key);

					if (tuple != null && tuple.GetRawValue(DbAttribute.DefaultModel.Index) == current) {
						tracker.Table.CommandSetModel(tuple, original, current);
						return;
					}
				}

				foreach (var table in Tables) {
					if (table.TryFindModel(current, out var tuple)) {
						table.CommandSetModel(tuple, original, current);
						break;
					}
				}
			}
		}

		internal static object GetValue(int key, BaseTable table, object model, string fieldName) {
			var result = TypeTreeHelper.GetValue(model, fieldName);

			if (result == null || result.Count == 0)
				throw DatabaseExceptions.CreateModelFieldNotFoundException(fieldName, model.GetType());

			if (table != null)
				TrackModel(key, table, model);

			var value = result.First();

			if (value == null) {
				var objectTree = TypeTreeHelper.GetObjectTrees(model, fieldName);
				var fieldInfo = objectTree.First().Member as FieldInfo;

				if (fieldInfo.FieldType == typeof(string)) {
					var valueString2 = (string)value;

					if (String.IsNullOrEmpty(valueString2))
						return 0;

					if (Int32.TryParse(valueString2, out int intValue))
						return intValue;
				}
			}

			if (value is string valueString) {
				if (String.IsNullOrEmpty(valueString))
					return 0;

				if (Int32.TryParse(valueString, out int intValue))
					return intValue;
			}
			else if (value is Enum valueEnum) {
				if (valueEnum.GetType().GetEnumUnderlyingType() == typeof(Int64)) {
					return (Int64)(object)valueEnum;
				}
				else if (valueEnum.GetType().GetEnumUnderlyingType() == typeof(Int32)) {
					return (Int32)(object)valueEnum;
				}
			}

			return value;
		}

		public static List<BaseTable> Tables = new List<BaseTable>();
		private static bool _enableTupleTrace;
	}

	public abstract class BaseTable {
		protected AttributeList _list;

		public AttributeList AttributeList => _list;

		protected BaseTable() {
			EnableEvents = true;
		}

		public object AttachedProperty { get; set; }
		public bool EnableEvents { get; set; }

		public virtual void ClearTupleStates() {
		}

		internal abstract bool Contains(Tuple tuple);
		internal abstract void CommandSet(Tuple tuple, DbAttribute attribute, object value);
		internal abstract void CommandSetModel(Tuple tuple, object modelOriginal, object modelCurrent);
		internal abstract bool TryFindModel(object model, out Tuple tuple);
		internal abstract Tuple TryFindTuple(object key);
	}
}

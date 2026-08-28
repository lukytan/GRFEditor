using System;
using System.Reflection;
using System.Linq.Expressions;
using System.Collections.Concurrent;

namespace Utilities {
	public static class ReflectionOptimizer<TModel, TFieldValue> {
		private static readonly ConcurrentDictionary<string, Func<TModel, TFieldValue>> Getters = new ConcurrentDictionary<string, Func<TModel, TFieldValue>>();
		private static readonly ConcurrentDictionary<string, Action<TModel, TFieldValue>> Setters = new ConcurrentDictionary<string, Action<TModel, TFieldValue>>();

		public static Func<TModel, TFieldValue> GetGetter(string fieldName) {
			return Getters.GetOrAdd(fieldName, name => {
				var param = Expression.Parameter(typeof(TModel), "model");
				var member = Expression.PropertyOrField(param, name);
				return Expression.Lambda<Func<TModel, TFieldValue>>(member, param).Compile();
			});
		}

		public static Action<TModel, TFieldValue> GetSetter(string fieldName) {
			return Setters.GetOrAdd(fieldName, name => {
				var modelParam = Expression.Parameter(typeof(TModel), "model");
				var valueParam = Expression.Parameter(typeof(TFieldValue), "value");
				var member = Expression.PropertyOrField(modelParam, name);

				var assign = Expression.Assign(member, valueParam);
				return Expression.Lambda<Action<TModel, TFieldValue>>(assign, modelParam, valueParam).Compile();
			});
		}
	}

	public static class ReflectionOptimizer<TFieldValue> {
		private static ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<object, TFieldValue>>> Getters = new ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<object, TFieldValue>>>();
		private static ConcurrentDictionary<Type, ConcurrentDictionary<string, Action<object, TFieldValue>>> Setters = new ConcurrentDictionary<Type, ConcurrentDictionary<string, Action<object, TFieldValue>>>();

		public static Func<object, TFieldValue> GetGetter(Type modelType, string fieldName) {
			if (!Getters.TryGetValue(modelType, out var getters)) {
				getters = new ConcurrentDictionary<string, Func<object, TFieldValue>>();
				Getters[modelType] = getters;
			}

			return getters.GetOrAdd(fieldName, name => {
				FieldInfo fi = modelType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (fi == null) throw new ArgumentException($"Field '{name}' not found on {modelType.Name}");

				var instanceParam = Expression.Parameter(typeof(object), "model");
				var castInstance = Expression.Convert(instanceParam, modelType);
				var fieldAccess = Expression.Field(castInstance, fi);

				// Handle conversion if the underlying field is not a string (optional safety)
				var castResult = Expression.Convert(fieldAccess, typeof(TFieldValue));

				return Expression.Lambda<Func<object, TFieldValue>>(castResult, instanceParam).Compile();
			});
		}

		public static Action<object, TFieldValue> GetSetter(Type modelType, string fieldName) {
			if (!Setters.TryGetValue(modelType, out var setters)) {
				setters = new ConcurrentDictionary<string, Action<object, TFieldValue>>();
				Setters[modelType] = setters;
			}

			return setters.GetOrAdd(fieldName, name => {
				FieldInfo fi = modelType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (fi == null) throw new ArgumentException($"Field '{name}' not found on {modelType.Name}");

				var instanceParam = Expression.Parameter(typeof(object), "model");
				var valueParam = Expression.Parameter(typeof(TFieldValue), "value");

				var castInstance = Expression.Convert(instanceParam, modelType);
				var fieldAccess = Expression.Field(castInstance, fi);

				// Assign the value to the field: model.Field = value
				var assignment = Expression.Assign(fieldAccess, valueParam);

				return Expression.Lambda<Action<object, TFieldValue>>(assignment, instanceParam, valueParam).Compile();
			});
		}
	}
}

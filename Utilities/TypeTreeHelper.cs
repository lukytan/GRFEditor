using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Utilities {
	public class ObjectTree {
		public MemberInfo Member;
		public bool IsCollection;
		public Dictionary<string, ObjectTree> FieldsOrMembers = new Dictionary<string, ObjectTree>(StringComparer.OrdinalIgnoreCase);
		internal Func<object, object> _compiledGetter;
		internal Action<object, object> _compiledSetter;

		public object GetValue(object target) {
			if (target == null || Member == null) return null;

			if (_compiledGetter != null) return _compiledGetter(target);

			return null;
		}

		public void SetValue(object target, object value) {
			if (target == null || Member == null) return;

			if (_compiledSetter != null) _compiledSetter(target, value);
		}
	}

	public static class TypeTreeHelper {
		public static Dictionary<Type, ObjectTree> Objects = new Dictionary<Type, ObjectTree>();

		public static ObjectTree GetObjectTree(Type type) {
			if (!Objects.TryGetValue(type, out ObjectTree objectTree)) {
				objectTree = ParseType(type);
				Objects[type] = objectTree;
			}

			return objectTree;
		}

		public static List<object> GetValue(object obj, string member) {
			var type = obj.GetType();

			if (!Objects.TryGetValue(type, out ObjectTree objectTree)) {
				objectTree = ParseType(type);
				Objects[type] = objectTree;
			}

			return GetValue(type, objectTree, obj, member);
		}

		public static List<ObjectTree> GetObjectTrees(object obj, string member) {
			var type = obj.GetType();

			if (!Objects.TryGetValue(type, out ObjectTree objectTree)) {
				objectTree = ParseType(type);
				Objects[type] = objectTree;
			}

			return GetObjectTrees(type, objectTree, obj, member);
		}

		public static void SetValue(object obj, string member, object value) {
			var type = obj.GetType();

			if (!Objects.TryGetValue(type, out ObjectTree objectTree)) {
				objectTree = ParseType(type);
				Objects[type] = objectTree;
			}

			SetValue(type, objectTree, obj, member, value);
		}

		public static List<ObjectTree> GetObjectTrees(Type type, ObjectTree parser, object obj, string member, List<ObjectTree> results = null) {
			if (results == null)
				results = new List<ObjectTree>();

			string[] members = member.Split('.');

			ObjectTree current = parser;

			for (int i = 0; i < members.Length; i++) {
				var currentMember = members[i];

				int indexBracketOpen = currentMember.IndexOf("[", StringComparison.OrdinalIgnoreCase);
				int indexBracketEnd = currentMember.IndexOf("]", StringComparison.OrdinalIgnoreCase);
				int index = -1;

				if (indexBracketOpen > 0 && indexBracketEnd > indexBracketOpen) {
					var indexStr = currentMember.Substring(indexBracketOpen + 1, indexBracketEnd - indexBracketOpen - 1);
					if (!Int32.TryParse(indexStr, out index))
						return null;
					currentMember = currentMember.Substring(0, indexBracketOpen);
				}

				if (!current.FieldsOrMembers.TryGetValue(currentMember, out current)) {
					return null;
				}

				obj = current.GetValue(obj);

				if (current.IsCollection && obj is IEnumerable collection) {
					if (i + 1 >= members.Length) {
						int count = 0;

						foreach (var entry in collection)
							count++;

						results.Add(current);
						break;
					}

					var newMember = members.Skip(i + 1).Aggregate((p, n) => p + "," + n);

					if (index > -1) {
						obj = GetElementAt(collection, index);

						if (obj == null)
							break;

						var objects = GetObjectTrees(obj, newMember);
						results.AddRange(objects);
					}
					else {
						foreach (var item in collection) {
							var objects = GetObjectTrees(item, newMember);

							if (objects == null)
								break;

							results.AddRange(objects);
						}
					}

					break;
				}
				else {
					//if (obj != null && i == members.Length - 1)
					if (i == members.Length - 1)
						results.Add(current);
				}
			}

			return results;
		}

		public static List<object> GetValue(Type type, ObjectTree parser, object obj, string member, List<object> results = null) {
			if (results == null)
				results = new List<object>();

			string[] members = member.Split('.');

			ObjectTree current = parser;

			for (int i = 0; i < members.Length; i++) {
				var currentMember = members[i];

				int indexBracketOpen = currentMember.IndexOf("[", StringComparison.OrdinalIgnoreCase);
				int indexBracketEnd = currentMember.IndexOf("]", StringComparison.OrdinalIgnoreCase);
				int index = -1;

				if (indexBracketOpen > 0 && indexBracketEnd > indexBracketOpen) {
					var indexStr = currentMember.Substring(indexBracketOpen + 1, indexBracketEnd - indexBracketOpen - 1);
					if (!Int32.TryParse(indexStr, out index))
						return null;
					currentMember = currentMember.Substring(0, indexBracketOpen);
				}

				if (!current.FieldsOrMembers.TryGetValue(currentMember, out current)) {
					return null;
				}

				obj = current.GetValue(obj);

				if (current.IsCollection && obj is IEnumerable collection) {
					if (i + 1 >= members.Length) {
						int count = 0;

						foreach (var entry in collection)
							count++;

						results.Add(count);
						break;
					}

					var newMember = members.Skip(i + 1).Aggregate((p, n) => p + "," + n);

					if (index > -1) {
						obj = GetElementAt(collection, index);

						if (obj == null)
							break;

						var objects = GetValue(obj, newMember);
						results.AddRange(objects);
					}
					else {
						foreach (var item in collection) {
							var objects = GetValue(item, newMember);

							if (objects == null)
								break;

							results.AddRange(objects);
						}
					}

					break;
				}
				else {
					//if (obj != null && i == members.Length - 1)
					if (i == members.Length - 1)
						results.Add(obj);
				}
			}

			return results;
		}

		public static void SetValue(Type type, ObjectTree parser, object obj, string member, object value) {
			string[] members = member.Split('.');

			ObjectTree current = parser;

			for (int i = 0; i < members.Length; i++) {
				var currentMember = members[i];

				int indexBracketOpen = currentMember.IndexOf("[", StringComparison.OrdinalIgnoreCase);
				int indexBracketEnd = currentMember.IndexOf("]", StringComparison.OrdinalIgnoreCase);
				int index = -1;

				if (indexBracketOpen > 0 && indexBracketEnd > indexBracketOpen) {
					var indexStr = currentMember.Substring(indexBracketOpen + 1, indexBracketEnd - indexBracketOpen - 1);
					if (!Int32.TryParse(indexStr, out index))
						return;
					currentMember = currentMember.Substring(0, indexBracketOpen);
				}

				if (!current.FieldsOrMembers.TryGetValue(currentMember, out current)) {
					return;
				}

				var parentObj = obj;
				obj = current.GetValue(obj);

				if (current.IsCollection && obj is IEnumerable collection) {
					if (i + 1 >= members.Length) {
						break;
					}

					var newMember = members.Skip(i + 1).Aggregate((p, n) => p + "," + n);

					if (index > -1) {
						if (collection is IList list) {
							list[index] = value;
						}
					}
					else {
						foreach (var item in collection) {
							SetValue(item, newMember, value);
						}
					}

					break;
				}
				else {
					if (i == members.Length - 1)
						current.SetValue(parentObj, value);
				}
			}
		}

		public static object GetElementAt(IEnumerable enumerable, int index) {
			if (index < 0) return null;

			int currentIndex = 0;
			IEnumerator enumerator = enumerable.GetEnumerator();

			try {
				while (enumerator.MoveNext()) {
					if (currentIndex == index) {
						return enumerator.Current;
					}
					currentIndex++;
				}
			}
			finally {
				var disposable = enumerator as IDisposable;
				if (disposable != null) disposable.Dispose();
			}

			return null;
		}

		public static ObjectTree ParseType(Type type, MemberInfo currentMember = null, Type parentType = null) {
			if (currentMember == null && Objects.TryGetValue(type, out ObjectTree objParser)) {
				return objParser;
			}

			var parser = new ObjectTree { Member = currentMember };

			if (currentMember == null) {
				Objects[type] = parser;
			}

			if (currentMember != null && parentType != null) {
				parser._compiledGetter = CompileGetter(parentType, currentMember);
				parser._compiledSetter = CompileSetter(parentType, currentMember);
			}

			if (type == typeof(string)) {
				return parser;
			}

			if (typeof(IEnumerable).IsAssignableFrom(type)) {
				parser.IsCollection = true;

				Type elementType = type.IsArray ? type.GetElementType() : null;

				if (elementType == null && type.IsGenericType) {
					elementType = type.GetGenericArguments()[0];
				}

				if (elementType != null && !elementType.IsPrimitive && elementType != typeof(string)) {
					var elementParser = ParseType(elementType);
					parser.FieldsOrMembers = elementParser.FieldsOrMembers;
				}

				return parser;
			}

			if (!type.IsPrimitive) {
				var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

				foreach (var field in type.GetFields(flags)) {
					parser.FieldsOrMembers[field.Name] = ParseType(field.FieldType, field, type);
				}

				foreach (var prop in type.GetProperties(flags)) {
					if (prop.CanRead) {
						parser.FieldsOrMembers[prop.Name] = ParseType(prop.PropertyType, prop, type);
					}
				}
			}

			return parser;
		}

		/// <summary>
		/// Compiles a highly optimized delegate equivalent to: (object target) => (object)((ParentType)target).Member
		/// </summary>
		private static Func<object, object> CompileGetter(Type parentType, MemberInfo member) {
			// 1. Define the input parameter: standard 'object'
			var inputParameter = Expression.Parameter(typeof(object), "target");

			// 2. Cast the input parameter from 'object' to the concrete declaring type
			var castTarget = Expression.Convert(inputParameter, parentType);

			// 3. Access the property or field
			Expression memberAccess = member is PropertyInfo prop
				? Expression.Property(castTarget, prop)
				: Expression.Field(castTarget, (FieldInfo)member);

			var boxResult = Expression.Convert(memberAccess, typeof(object));

			// 5. Compile into a ready-to-use Func<object, object>
			return Expression.Lambda<Func<object, object>>(boxResult, inputParameter).Compile();
		}

		/// <summary>
		/// Compiles a highly optimized delegate equivalent to: (object target, object value) => ((ParentType)target).Member = (MemberType)value;
		/// </summary>
		private static Action<object, object> CompileSetter(Type parentType, MemberInfo member) {
			// 1. Define the input parameter: standard 'object'
			var targetParameter = Expression.Parameter(typeof(object), "target");
			var valueParameter = Expression.Parameter(typeof(object), "value");

			// 2. Cast the input parameter from 'object' to the concrete declaring type
			Expression castTarget = parentType.IsValueType
				? Expression.Unbox(targetParameter, parentType)
				: Expression.Convert(targetParameter, parentType);

			// 3. Resolve the specific member info and member type
			Expression memberAccess;
			Type memberType;

			if (member is PropertyInfo prop) {
				return null;
				//memberAccess = Expression.Property(castTarget, prop);
				//memberType = prop.PropertyType;
			}
			else if (member is FieldInfo field) {
				memberAccess = Expression.Field(castTarget, field);
				memberType = field.FieldType;
			}
			else {
				throw new ArgumentException("Member must be a PropertyInfo or FieldInfo", nameof(member));
			}

			// 4. Cast the incoming boxed value to the exact member type
			var castValue = Expression.Convert(valueParameter, memberType);

			// 5. Create the assignment expression: target.Member = value
			var assignExpression = Expression.Assign(memberAccess, castValue);

			// 6. Compile into a ready-to-use Action<object, object>
			return Expression.Lambda<Action<object, object>>(assignExpression, targetParameter, valueParameter).Compile();
		}
	}
}

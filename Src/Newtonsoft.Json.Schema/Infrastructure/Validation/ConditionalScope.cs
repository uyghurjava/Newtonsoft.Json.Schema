#region License
// Copyright (c) Newtonsoft. All Rights Reserved.
// License: https://raw.github.com/JamesNK/Newtonsoft.Json.Schema/master/LICENSE.md
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json.Utilities;

namespace Newtonsoft.Json.Schema.Infrastructure.Validation
{
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    internal abstract class ConditionalScope : Scope
    {
        protected ConditionalContext ConditionalContext;
        protected SchemaScope ParentSchemaScope;
        protected readonly List<SchemaScope> ChildScopes;

        protected ConditionalScope()
        {
            ChildScopes = new List<SchemaScope>();
        }

        internal string DebuggerDisplay => GetType().Name + " - Complete=" + Complete
#if DEBUG
                                           + " - ScopeId=" + DebugId
#endif
        ;

        public override void Initialize(ContextBase context, SchemaScope parent, int initialDepth, ScopeType type)
        {
            base.Initialize(context, parent, initialDepth, type);

            ChildScopes.Clear();
            ParentSchemaScope = parent;
            ConditionalContext = ConditionalContext.Create(context);
        }

        public void InitializeScopes(JsonToken token, List<JSchema> schemas, int scopeIndex)
        {
            foreach (JSchema schema in schemas)
            {
                // cache this for performance
                int scopeCurrentIndex = scopeIndex;

                // check to see whether a scope with the same schema exists
                SchemaScope childScope = GetExistingSchemaScope(schema, ref scopeCurrentIndex);

                if (childScope == null)
                {
                    childScope = SchemaScope.CreateTokenScope(token, schema, ConditionalContext, null, InitialDepth);
                }

#if DEBUG
                childScope.ConditionalParents.Add(this);
#endif

                ChildScopes.Add(childScope);
            }
        }

        private SchemaScope GetExistingSchemaScope(JSchema schema, ref int scopeCurrentIndex)
        {
            for (int i = Context.Scopes.Count - 1; i >= 0; i--)
            {
                if (Context.Scopes[i] is SchemaScope scope)
                {
                    if (scope.InitialDepth == InitialDepth)
                    {
                        if (!scope.Complete && scope.Schema == schema)
                        {
                            if (i < scopeCurrentIndex)
                            {
                                // existing schema is before conditional scope
                                // move it so conditional scope is evaluated after existing schema
                                Context.Scopes.RemoveAt(i);
                                Context.Scopes.Insert(scopeCurrentIndex, scope);

#if DEBUG
                                // sanity check that moving the scope won't cause order of evaluation errors
                                for (int j = scopeCurrentIndex - 1; j >= 0; j--)
                                {
                                    if (Context.Scopes[j].Parent == scope)
                                    {
                                        throw new Exception("Child will be evaluated after parent.");
                                    }
                                }
#endif

                                // decrement index because the schema before current scope has been moved to after
                                scopeCurrentIndex--;
                            }

                            return scope;
                        }
                    }
                    else if (scope.InitialDepth < InitialDepth)
                    {
                        break;
                    }
                }
            }

            return null;
        }

        protected int GetChildrenValidCount(JsonToken token, object value, int depth)
        {
            int count = 0;
            for (int i = 0; i < ChildScopes.Count; i++)
            {
                SchemaScope schemaScope = ChildScopes[i];
                AssertScopeComplete(schemaScope, token, value, depth);

                if (schemaScope.IsValid)
                {
                    count++;
                }
            }

            return count;
        }

        protected bool GetChildrenAnyValid(JsonToken token, object value, int depth)
        {
            for (int i = 0; i < ChildScopes.Count; i++)
            {
                SchemaScope schemaScope = ChildScopes[i];
                AssertScopeComplete(schemaScope, token, value, depth);

                if (schemaScope.IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        protected bool GetChildrenAllValid(JsonToken token, object value, int depth)
        {
            for (int i = 0; i < ChildScopes.Count; i++)
            {
                SchemaScope schemaScope = ChildScopes[i];
                AssertScopeComplete(schemaScope, token, value, depth);

                if (!schemaScope.IsValid)
                {
                    return false;
                }
            }

            return true;
        }

        protected SchemaScope GetSchemaScopeBySchema(JSchema schema, JsonToken token, object value, int depth)
        {
            for (int i = 0; i < ChildScopes.Count; i++)
            {
                SchemaScope schemaScope = ChildScopes[i];

                if (schemaScope.Schema == schema)
                {
                    AssertScopeComplete(schemaScope, token, value, depth);
                    return schemaScope;
                }
            }

            return null;
        }

        private void AssertScopeComplete(SchemaScope schemaScope, JsonToken token, object value, int depth)
        {
            // the schema scope that the conditional scope depends on may not be complete because it has be re-ordered
            // schema scope will be at the same depth at the conditional so evaluate it immediately
            if (!schemaScope.Complete)
            {
                schemaScope.EvaluateToken(token, value, depth);

#if DEBUG
                if (!schemaScope.Complete)
                {
                    throw new Exception("Schema scope {0} is not complete.".FormatWith(CultureInfo.InvariantCulture, schemaScope.DebugId));
                }
#endif
            }
        }
    }
}
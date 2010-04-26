﻿/**
 *   Copyright (c) Rich Hickey. All rights reserved.
 *   The use and distribution terms for this software are covered by the
 *   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
 *   which can be found in the file epl-v10.html at the root of this distribution.
 *   By using this software in any fashion, you are agreeing to be bound by
 * 	 the terms of this license.
 *   You must not remove this notice, or any other, from this software.
 **/

/**
 *   Author: David Miller
 **/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection.Emit;
using System.Reflection;
#if CLR2
using Microsoft.Scripting.Ast;
#else
using System.Linq.Expressions;
#endif
using Microsoft.Scripting.Generation;

namespace clojure.lang.CljCompiler.Ast
{
    abstract class ObjMethod
    {
        #region Data

        // Java: when closures are defined inside other closures,
        // the closed over locals need to be propagated to the enclosing fn
        readonly ObjMethod _parent;
        
        internal ObjMethod Parent
        {
            get { return _parent; }
        }
        
        IPersistentMap _locals = null;       // localbinding => localbinding
        IPersistentMap _indexLocals = null;  // num -> localbinding


        public IPersistentMap Locals
        {
            get { return _locals; }
            set { _locals = value; }
        }

        public IPersistentMap IndexLocals
        {
            get { return _indexLocals; }
            set { _indexLocals = value; }
        }

        protected LocalBinding _thisBinding;

        protected Expr _body = null;
        
        ObjExpr _objx;
        internal ObjExpr Objx
        {
            get { return _objx; }
            //set { _objx = value; }
        }

        protected IPersistentVector _argLocals;
        int _maxLocal = 0;

        public int MaxLocal
        {
            get { return _maxLocal; }
            set { _maxLocal = value; }
        }

        IPersistentSet _localsUsedInCatchFinally = PersistentHashSet.EMPTY;

        public IPersistentSet LocalsUsedInCatchFinally
        {
            get { return _localsUsedInCatchFinally; }
            set { _localsUsedInCatchFinally = value; }
        }

        #endregion

        #region abstract methods

        internal abstract bool IsVariadic { get; }
        internal abstract int NumParams { get; }
        internal abstract int RequiredArity { get; }
        internal abstract string MethodName { get; }

        #endregion

        #region not yet

        /*        
         * 
         * 
                 internal bool IsVariadic
        {
            get { return _restParm != null; }
        }


        internal int NumParams
        {
            get { return _reqParms.count() + (IsVariadic ? 1 : 0); }
        }

        internal int RequiredArity
        {
            get { return _reqParms.count(); }
        } 


        //int _line;



        internal bool IsVariadic
        {
            get { return _restParm != null; }
        }


        internal int NumParams
        {
            get { return _reqParms.count() + (IsVariadic ? 1 : 0); }
        }

        internal int RequiredArity
        {
            get { return _reqParms.count(); }
        }
        */


        #endregion

        #region Ctors

        public ObjMethod(ObjExpr fn, ObjMethod parent)
        {
            _parent = parent;
            _objx = fn;
        }

        #endregion

        #region Code generation

        internal void GenerateCode(GenContext context)
        {
            MethodBuilder mb = GenerateStaticMethod(context);
            GenerateMethod(mb, context);
        }


        MethodBuilder GenerateStaticMethod(GenContext context)
        {
            string methodName = GetStaticMethodName();
            ObjExpr fn = context.ObjExpr;
            TypeBuilder tb = fn.TypeBuilder;

            List<ParameterExpression> parms = new List<ParameterExpression>(_argLocals.count() + 1);

            ParameterExpression thisParm = Expression.Parameter(fn.BaseType, "this");
            _thisBinding.ParamExpression = thisParm;
            fn.ThisParam = thisParm;
            parms.Add(thisParm);

            try
            {
                LabelTarget loopLabel = Expression.Label("top");

                Var.pushThreadBindings(RT.map(Compiler.LOOP_LABEL, loopLabel, Compiler.METHOD, this));

                for (int i = 0; i < _argLocals.count(); i++)
                {
                    LocalBinding lb = (LocalBinding)_argLocals.nth(i);
                    ParameterExpression parm = Expression.Parameter(typeof(object), lb.Name);
                    lb.ParamExpression = parm;
                    parms.Add(parm);
                }

                Expression body =
                    Expression.Block(
                        Expression.Label(loopLabel),
                        Compiler.MaybeBox(_body.GenDlr(context)));
                LambdaExpression lambda = Expression.Lambda(body, parms);
                // JVM: Clears locals here.


                // TODO: Cache all the CreateObjectTypeArray values
                MethodBuilder mb = tb.DefineMethod(methodName, MethodAttributes.Static, typeof(object), Compiler.CreateObjectTypeArray(NumParams));

                lambda.CompileToMethod(mb, true);
                return mb;
            }
            finally
            {
                Var.popThreadBindings();
            }

        }

        private string GetStaticMethodName()
        {
            return String.Format("__invokeHelper_{0}{1}", RequiredArity, IsVariadic ? "v" : string.Empty);
        }


        void GenerateMethod(MethodInfo staticMethodInfo, GenContext context)
        {
            //string methodName = IsVariadic ? "doInvoke" : "invoke";

            TypeBuilder tb = context.ObjExpr.TypeBuilder;

            // TODO: Cache all the CreateObjectTypeArray values
            MethodBuilder mb = tb.DefineMethod(MethodName, MethodAttributes.ReuseSlot | MethodAttributes.Public | MethodAttributes.Virtual, typeof(object), Compiler.CreateObjectTypeArray(NumParams));
            ILGen gen = new ILGen(mb.GetILGenerator());
            gen.EmitLoadArg(0);                             
            for (int i = 1; i <= _argLocals.count(); i++)
                gen.EmitLoadArg(i);                         
            gen.EmitCall(staticMethodInfo);                 
            gen.Emit(OpCodes.Ret);
        }

        #endregion

    }
}
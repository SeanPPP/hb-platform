var __create = Object.create;
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __getProtoOf = Object.getPrototypeOf;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __commonJS = (cb, mod) => function __require() {
  return mod || (0, cb[__getOwnPropNames(cb)[0]])((mod = { exports: {} }).exports, mod), mod.exports;
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toESM = (mod, isNodeMode, target) => (target = mod != null ? __create(__getProtoOf(mod)) : {}, __copyProps(
  // If the importer is in node compatibility mode or this is not an ESM
  // file that has been converted to a CommonJS file using a Babel-
  // compatible transform (i.e. "__esModule" has not been set), then set
  // "default" to the CommonJS "module.exports" for node compatibility.
  isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target,
  mod
));

// node_modules/react/cjs/react.production.min.js
var require_react_production_min = __commonJS({
  "node_modules/react/cjs/react.production.min.js"(exports) {
    "use strict";
    var l = Symbol.for("react.element");
    var n = Symbol.for("react.portal");
    var p = Symbol.for("react.fragment");
    var q = Symbol.for("react.strict_mode");
    var r = Symbol.for("react.profiler");
    var t = Symbol.for("react.provider");
    var u = Symbol.for("react.context");
    var v = Symbol.for("react.forward_ref");
    var w = Symbol.for("react.suspense");
    var x = Symbol.for("react.memo");
    var y = Symbol.for("react.lazy");
    var z = Symbol.iterator;
    function A(a) {
      if (null === a || "object" !== typeof a) return null;
      a = z && a[z] || a["@@iterator"];
      return "function" === typeof a ? a : null;
    }
    var B = { isMounted: function() {
      return false;
    }, enqueueForceUpdate: function() {
    }, enqueueReplaceState: function() {
    }, enqueueSetState: function() {
    } };
    var C = Object.assign;
    var D = {};
    function E(a, b, e) {
      this.props = a;
      this.context = b;
      this.refs = D;
      this.updater = e || B;
    }
    E.prototype.isReactComponent = {};
    E.prototype.setState = function(a, b) {
      if ("object" !== typeof a && "function" !== typeof a && null != a) throw Error("setState(...): takes an object of state variables to update or a function which returns an object of state variables.");
      this.updater.enqueueSetState(this, a, b, "setState");
    };
    E.prototype.forceUpdate = function(a) {
      this.updater.enqueueForceUpdate(this, a, "forceUpdate");
    };
    function F() {
    }
    F.prototype = E.prototype;
    function G(a, b, e) {
      this.props = a;
      this.context = b;
      this.refs = D;
      this.updater = e || B;
    }
    var H = G.prototype = new F();
    H.constructor = G;
    C(H, E.prototype);
    H.isPureReactComponent = true;
    var I = Array.isArray;
    var J = Object.prototype.hasOwnProperty;
    var K = { current: null };
    var L = { key: true, ref: true, __self: true, __source: true };
    function M(a, b, e) {
      var d, c = {}, k = null, h = null;
      if (null != b) for (d in void 0 !== b.ref && (h = b.ref), void 0 !== b.key && (k = "" + b.key), b) J.call(b, d) && !L.hasOwnProperty(d) && (c[d] = b[d]);
      var g = arguments.length - 2;
      if (1 === g) c.children = e;
      else if (1 < g) {
        for (var f = Array(g), m = 0; m < g; m++) f[m] = arguments[m + 2];
        c.children = f;
      }
      if (a && a.defaultProps) for (d in g = a.defaultProps, g) void 0 === c[d] && (c[d] = g[d]);
      return { $$typeof: l, type: a, key: k, ref: h, props: c, _owner: K.current };
    }
    function N(a, b) {
      return { $$typeof: l, type: a.type, key: b, ref: a.ref, props: a.props, _owner: a._owner };
    }
    function O(a) {
      return "object" === typeof a && null !== a && a.$$typeof === l;
    }
    function escape(a) {
      var b = { "=": "=0", ":": "=2" };
      return "$" + a.replace(/[=:]/g, function(a2) {
        return b[a2];
      });
    }
    var P = /\/+/g;
    function Q(a, b) {
      return "object" === typeof a && null !== a && null != a.key ? escape("" + a.key) : b.toString(36);
    }
    function R(a, b, e, d, c) {
      var k = typeof a;
      if ("undefined" === k || "boolean" === k) a = null;
      var h = false;
      if (null === a) h = true;
      else switch (k) {
        case "string":
        case "number":
          h = true;
          break;
        case "object":
          switch (a.$$typeof) {
            case l:
            case n:
              h = true;
          }
      }
      if (h) return h = a, c = c(h), a = "" === d ? "." + Q(h, 0) : d, I(c) ? (e = "", null != a && (e = a.replace(P, "$&/") + "/"), R(c, b, e, "", function(a2) {
        return a2;
      })) : null != c && (O(c) && (c = N(c, e + (!c.key || h && h.key === c.key ? "" : ("" + c.key).replace(P, "$&/") + "/") + a)), b.push(c)), 1;
      h = 0;
      d = "" === d ? "." : d + ":";
      if (I(a)) for (var g = 0; g < a.length; g++) {
        k = a[g];
        var f = d + Q(k, g);
        h += R(k, b, e, f, c);
      }
      else if (f = A(a), "function" === typeof f) for (a = f.call(a), g = 0; !(k = a.next()).done; ) k = k.value, f = d + Q(k, g++), h += R(k, b, e, f, c);
      else if ("object" === k) throw b = String(a), Error("Objects are not valid as a React child (found: " + ("[object Object]" === b ? "object with keys {" + Object.keys(a).join(", ") + "}" : b) + "). If you meant to render a collection of children, use an array instead.");
      return h;
    }
    function S(a, b, e) {
      if (null == a) return a;
      var d = [], c = 0;
      R(a, d, "", "", function(a2) {
        return b.call(e, a2, c++);
      });
      return d;
    }
    function T(a) {
      if (-1 === a._status) {
        var b = a._result;
        b = b();
        b.then(function(b2) {
          if (0 === a._status || -1 === a._status) a._status = 1, a._result = b2;
        }, function(b2) {
          if (0 === a._status || -1 === a._status) a._status = 2, a._result = b2;
        });
        -1 === a._status && (a._status = 0, a._result = b);
      }
      if (1 === a._status) return a._result.default;
      throw a._result;
    }
    var U = { current: null };
    var V = { transition: null };
    var W = { ReactCurrentDispatcher: U, ReactCurrentBatchConfig: V, ReactCurrentOwner: K };
    function X() {
      throw Error("act(...) is not supported in production builds of React.");
    }
    exports.Children = { map: S, forEach: function(a, b, e) {
      S(a, function() {
        b.apply(this, arguments);
      }, e);
    }, count: function(a) {
      var b = 0;
      S(a, function() {
        b++;
      });
      return b;
    }, toArray: function(a) {
      return S(a, function(a2) {
        return a2;
      }) || [];
    }, only: function(a) {
      if (!O(a)) throw Error("React.Children.only expected to receive a single React element child.");
      return a;
    } };
    exports.Component = E;
    exports.Fragment = p;
    exports.Profiler = r;
    exports.PureComponent = G;
    exports.StrictMode = q;
    exports.Suspense = w;
    exports.__SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED = W;
    exports.act = X;
    exports.cloneElement = function(a, b, e) {
      if (null === a || void 0 === a) throw Error("React.cloneElement(...): The argument must be a React element, but you passed " + a + ".");
      var d = C({}, a.props), c = a.key, k = a.ref, h = a._owner;
      if (null != b) {
        void 0 !== b.ref && (k = b.ref, h = K.current);
        void 0 !== b.key && (c = "" + b.key);
        if (a.type && a.type.defaultProps) var g = a.type.defaultProps;
        for (f in b) J.call(b, f) && !L.hasOwnProperty(f) && (d[f] = void 0 === b[f] && void 0 !== g ? g[f] : b[f]);
      }
      var f = arguments.length - 2;
      if (1 === f) d.children = e;
      else if (1 < f) {
        g = Array(f);
        for (var m = 0; m < f; m++) g[m] = arguments[m + 2];
        d.children = g;
      }
      return { $$typeof: l, type: a.type, key: c, ref: k, props: d, _owner: h };
    };
    exports.createContext = function(a) {
      a = { $$typeof: u, _currentValue: a, _currentValue2: a, _threadCount: 0, Provider: null, Consumer: null, _defaultValue: null, _globalName: null };
      a.Provider = { $$typeof: t, _context: a };
      return a.Consumer = a;
    };
    exports.createElement = M;
    exports.createFactory = function(a) {
      var b = M.bind(null, a);
      b.type = a;
      return b;
    };
    exports.createRef = function() {
      return { current: null };
    };
    exports.forwardRef = function(a) {
      return { $$typeof: v, render: a };
    };
    exports.isValidElement = O;
    exports.lazy = function(a) {
      return { $$typeof: y, _payload: { _status: -1, _result: a }, _init: T };
    };
    exports.memo = function(a, b) {
      return { $$typeof: x, type: a, compare: void 0 === b ? null : b };
    };
    exports.startTransition = function(a) {
      var b = V.transition;
      V.transition = {};
      try {
        a();
      } finally {
        V.transition = b;
      }
    };
    exports.unstable_act = X;
    exports.useCallback = function(a, b) {
      return U.current.useCallback(a, b);
    };
    exports.useContext = function(a) {
      return U.current.useContext(a);
    };
    exports.useDebugValue = function() {
    };
    exports.useDeferredValue = function(a) {
      return U.current.useDeferredValue(a);
    };
    exports.useEffect = function(a, b) {
      return U.current.useEffect(a, b);
    };
    exports.useId = function() {
      return U.current.useId();
    };
    exports.useImperativeHandle = function(a, b, e) {
      return U.current.useImperativeHandle(a, b, e);
    };
    exports.useInsertionEffect = function(a, b) {
      return U.current.useInsertionEffect(a, b);
    };
    exports.useLayoutEffect = function(a, b) {
      return U.current.useLayoutEffect(a, b);
    };
    exports.useMemo = function(a, b) {
      return U.current.useMemo(a, b);
    };
    exports.useReducer = function(a, b, e) {
      return U.current.useReducer(a, b, e);
    };
    exports.useRef = function(a) {
      return U.current.useRef(a);
    };
    exports.useState = function(a) {
      return U.current.useState(a);
    };
    exports.useSyncExternalStore = function(a, b, e) {
      return U.current.useSyncExternalStore(a, b, e);
    };
    exports.useTransition = function() {
      return U.current.useTransition();
    };
    exports.version = "18.3.1";
  }
});

// node_modules/react/cjs/react.development.js
var require_react_development = __commonJS({
  "node_modules/react/cjs/react.development.js"(exports, module) {
    "use strict";
    if (process.env.NODE_ENV !== "production") {
      (function() {
        "use strict";
        if (typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ !== "undefined" && typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStart === "function") {
          __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStart(new Error());
        }
        var ReactVersion = "18.3.1";
        var REACT_ELEMENT_TYPE = Symbol.for("react.element");
        var REACT_PORTAL_TYPE = Symbol.for("react.portal");
        var REACT_FRAGMENT_TYPE = Symbol.for("react.fragment");
        var REACT_STRICT_MODE_TYPE = Symbol.for("react.strict_mode");
        var REACT_PROFILER_TYPE = Symbol.for("react.profiler");
        var REACT_PROVIDER_TYPE = Symbol.for("react.provider");
        var REACT_CONTEXT_TYPE = Symbol.for("react.context");
        var REACT_FORWARD_REF_TYPE = Symbol.for("react.forward_ref");
        var REACT_SUSPENSE_TYPE = Symbol.for("react.suspense");
        var REACT_SUSPENSE_LIST_TYPE = Symbol.for("react.suspense_list");
        var REACT_MEMO_TYPE = Symbol.for("react.memo");
        var REACT_LAZY_TYPE = Symbol.for("react.lazy");
        var REACT_OFFSCREEN_TYPE = Symbol.for("react.offscreen");
        var MAYBE_ITERATOR_SYMBOL = Symbol.iterator;
        var FAUX_ITERATOR_SYMBOL = "@@iterator";
        function getIteratorFn(maybeIterable) {
          if (maybeIterable === null || typeof maybeIterable !== "object") {
            return null;
          }
          var maybeIterator = MAYBE_ITERATOR_SYMBOL && maybeIterable[MAYBE_ITERATOR_SYMBOL] || maybeIterable[FAUX_ITERATOR_SYMBOL];
          if (typeof maybeIterator === "function") {
            return maybeIterator;
          }
          return null;
        }
        var ReactCurrentDispatcher = {
          /**
           * @internal
           * @type {ReactComponent}
           */
          current: null
        };
        var ReactCurrentBatchConfig = {
          transition: null
        };
        var ReactCurrentActQueue = {
          current: null,
          // Used to reproduce behavior of `batchedUpdates` in legacy mode.
          isBatchingLegacy: false,
          didScheduleLegacyUpdate: false
        };
        var ReactCurrentOwner = {
          /**
           * @internal
           * @type {ReactComponent}
           */
          current: null
        };
        var ReactDebugCurrentFrame = {};
        var currentExtraStackFrame = null;
        function setExtraStackFrame(stack) {
          {
            currentExtraStackFrame = stack;
          }
        }
        {
          ReactDebugCurrentFrame.setExtraStackFrame = function(stack) {
            {
              currentExtraStackFrame = stack;
            }
          };
          ReactDebugCurrentFrame.getCurrentStack = null;
          ReactDebugCurrentFrame.getStackAddendum = function() {
            var stack = "";
            if (currentExtraStackFrame) {
              stack += currentExtraStackFrame;
            }
            var impl = ReactDebugCurrentFrame.getCurrentStack;
            if (impl) {
              stack += impl() || "";
            }
            return stack;
          };
        }
        var enableScopeAPI = false;
        var enableCacheElement = false;
        var enableTransitionTracing = false;
        var enableLegacyHidden = false;
        var enableDebugTracing = false;
        var ReactSharedInternals = {
          ReactCurrentDispatcher,
          ReactCurrentBatchConfig,
          ReactCurrentOwner
        };
        {
          ReactSharedInternals.ReactDebugCurrentFrame = ReactDebugCurrentFrame;
          ReactSharedInternals.ReactCurrentActQueue = ReactCurrentActQueue;
        }
        function warn(format) {
          {
            {
              for (var _len = arguments.length, args = new Array(_len > 1 ? _len - 1 : 0), _key = 1; _key < _len; _key++) {
                args[_key - 1] = arguments[_key];
              }
              printWarning("warn", format, args);
            }
          }
        }
        function error(format) {
          {
            {
              for (var _len2 = arguments.length, args = new Array(_len2 > 1 ? _len2 - 1 : 0), _key2 = 1; _key2 < _len2; _key2++) {
                args[_key2 - 1] = arguments[_key2];
              }
              printWarning("error", format, args);
            }
          }
        }
        function printWarning(level, format, args) {
          {
            var ReactDebugCurrentFrame2 = ReactSharedInternals.ReactDebugCurrentFrame;
            var stack = ReactDebugCurrentFrame2.getStackAddendum();
            if (stack !== "") {
              format += "%s";
              args = args.concat([stack]);
            }
            var argsWithFormat = args.map(function(item) {
              return String(item);
            });
            argsWithFormat.unshift("Warning: " + format);
            Function.prototype.apply.call(console[level], console, argsWithFormat);
          }
        }
        var didWarnStateUpdateForUnmountedComponent = {};
        function warnNoop(publicInstance, callerName) {
          {
            var _constructor = publicInstance.constructor;
            var componentName = _constructor && (_constructor.displayName || _constructor.name) || "ReactClass";
            var warningKey = componentName + "." + callerName;
            if (didWarnStateUpdateForUnmountedComponent[warningKey]) {
              return;
            }
            error("Can't call %s on a component that is not yet mounted. This is a no-op, but it might indicate a bug in your application. Instead, assign to `this.state` directly or define a `state = {};` class property with the desired state in the %s component.", callerName, componentName);
            didWarnStateUpdateForUnmountedComponent[warningKey] = true;
          }
        }
        var ReactNoopUpdateQueue = {
          /**
           * Checks whether or not this composite component is mounted.
           * @param {ReactClass} publicInstance The instance we want to test.
           * @return {boolean} True if mounted, false otherwise.
           * @protected
           * @final
           */
          isMounted: function(publicInstance) {
            return false;
          },
          /**
           * Forces an update. This should only be invoked when it is known with
           * certainty that we are **not** in a DOM transaction.
           *
           * You may want to call this when you know that some deeper aspect of the
           * component's state has changed but `setState` was not called.
           *
           * This will not invoke `shouldComponentUpdate`, but it will invoke
           * `componentWillUpdate` and `componentDidUpdate`.
           *
           * @param {ReactClass} publicInstance The instance that should rerender.
           * @param {?function} callback Called after component is updated.
           * @param {?string} callerName name of the calling function in the public API.
           * @internal
           */
          enqueueForceUpdate: function(publicInstance, callback, callerName) {
            warnNoop(publicInstance, "forceUpdate");
          },
          /**
           * Replaces all of the state. Always use this or `setState` to mutate state.
           * You should treat `this.state` as immutable.
           *
           * There is no guarantee that `this.state` will be immediately updated, so
           * accessing `this.state` after calling this method may return the old value.
           *
           * @param {ReactClass} publicInstance The instance that should rerender.
           * @param {object} completeState Next state.
           * @param {?function} callback Called after component is updated.
           * @param {?string} callerName name of the calling function in the public API.
           * @internal
           */
          enqueueReplaceState: function(publicInstance, completeState, callback, callerName) {
            warnNoop(publicInstance, "replaceState");
          },
          /**
           * Sets a subset of the state. This only exists because _pendingState is
           * internal. This provides a merging strategy that is not available to deep
           * properties which is confusing. TODO: Expose pendingState or don't use it
           * during the merge.
           *
           * @param {ReactClass} publicInstance The instance that should rerender.
           * @param {object} partialState Next partial state to be merged with state.
           * @param {?function} callback Called after component is updated.
           * @param {?string} Name of the calling function in the public API.
           * @internal
           */
          enqueueSetState: function(publicInstance, partialState, callback, callerName) {
            warnNoop(publicInstance, "setState");
          }
        };
        var assign = Object.assign;
        var emptyObject = {};
        {
          Object.freeze(emptyObject);
        }
        function Component(props, context, updater) {
          this.props = props;
          this.context = context;
          this.refs = emptyObject;
          this.updater = updater || ReactNoopUpdateQueue;
        }
        Component.prototype.isReactComponent = {};
        Component.prototype.setState = function(partialState, callback) {
          if (typeof partialState !== "object" && typeof partialState !== "function" && partialState != null) {
            throw new Error("setState(...): takes an object of state variables to update or a function which returns an object of state variables.");
          }
          this.updater.enqueueSetState(this, partialState, callback, "setState");
        };
        Component.prototype.forceUpdate = function(callback) {
          this.updater.enqueueForceUpdate(this, callback, "forceUpdate");
        };
        {
          var deprecatedAPIs = {
            isMounted: ["isMounted", "Instead, make sure to clean up subscriptions and pending requests in componentWillUnmount to prevent memory leaks."],
            replaceState: ["replaceState", "Refactor your code to use setState instead (see https://github.com/facebook/react/issues/3236)."]
          };
          var defineDeprecationWarning = function(methodName, info) {
            Object.defineProperty(Component.prototype, methodName, {
              get: function() {
                warn("%s(...) is deprecated in plain JavaScript React classes. %s", info[0], info[1]);
                return void 0;
              }
            });
          };
          for (var fnName in deprecatedAPIs) {
            if (deprecatedAPIs.hasOwnProperty(fnName)) {
              defineDeprecationWarning(fnName, deprecatedAPIs[fnName]);
            }
          }
        }
        function ComponentDummy() {
        }
        ComponentDummy.prototype = Component.prototype;
        function PureComponent(props, context, updater) {
          this.props = props;
          this.context = context;
          this.refs = emptyObject;
          this.updater = updater || ReactNoopUpdateQueue;
        }
        var pureComponentPrototype = PureComponent.prototype = new ComponentDummy();
        pureComponentPrototype.constructor = PureComponent;
        assign(pureComponentPrototype, Component.prototype);
        pureComponentPrototype.isPureReactComponent = true;
        function createRef() {
          var refObject = {
            current: null
          };
          {
            Object.seal(refObject);
          }
          return refObject;
        }
        var isArrayImpl = Array.isArray;
        function isArray(a) {
          return isArrayImpl(a);
        }
        function typeName(value) {
          {
            var hasToStringTag = typeof Symbol === "function" && Symbol.toStringTag;
            var type = hasToStringTag && value[Symbol.toStringTag] || value.constructor.name || "Object";
            return type;
          }
        }
        function willCoercionThrow(value) {
          {
            try {
              testStringCoercion(value);
              return false;
            } catch (e) {
              return true;
            }
          }
        }
        function testStringCoercion(value) {
          return "" + value;
        }
        function checkKeyStringCoercion(value) {
          {
            if (willCoercionThrow(value)) {
              error("The provided key is an unsupported type %s. This value must be coerced to a string before before using it here.", typeName(value));
              return testStringCoercion(value);
            }
          }
        }
        function getWrappedName(outerType, innerType, wrapperName) {
          var displayName = outerType.displayName;
          if (displayName) {
            return displayName;
          }
          var functionName = innerType.displayName || innerType.name || "";
          return functionName !== "" ? wrapperName + "(" + functionName + ")" : wrapperName;
        }
        function getContextName(type) {
          return type.displayName || "Context";
        }
        function getComponentNameFromType(type) {
          if (type == null) {
            return null;
          }
          {
            if (typeof type.tag === "number") {
              error("Received an unexpected object in getComponentNameFromType(). This is likely a bug in React. Please file an issue.");
            }
          }
          if (typeof type === "function") {
            return type.displayName || type.name || null;
          }
          if (typeof type === "string") {
            return type;
          }
          switch (type) {
            case REACT_FRAGMENT_TYPE:
              return "Fragment";
            case REACT_PORTAL_TYPE:
              return "Portal";
            case REACT_PROFILER_TYPE:
              return "Profiler";
            case REACT_STRICT_MODE_TYPE:
              return "StrictMode";
            case REACT_SUSPENSE_TYPE:
              return "Suspense";
            case REACT_SUSPENSE_LIST_TYPE:
              return "SuspenseList";
          }
          if (typeof type === "object") {
            switch (type.$$typeof) {
              case REACT_CONTEXT_TYPE:
                var context = type;
                return getContextName(context) + ".Consumer";
              case REACT_PROVIDER_TYPE:
                var provider = type;
                return getContextName(provider._context) + ".Provider";
              case REACT_FORWARD_REF_TYPE:
                return getWrappedName(type, type.render, "ForwardRef");
              case REACT_MEMO_TYPE:
                var outerName = type.displayName || null;
                if (outerName !== null) {
                  return outerName;
                }
                return getComponentNameFromType(type.type) || "Memo";
              case REACT_LAZY_TYPE: {
                var lazyComponent = type;
                var payload = lazyComponent._payload;
                var init = lazyComponent._init;
                try {
                  return getComponentNameFromType(init(payload));
                } catch (x) {
                  return null;
                }
              }
            }
          }
          return null;
        }
        var hasOwnProperty = Object.prototype.hasOwnProperty;
        var RESERVED_PROPS = {
          key: true,
          ref: true,
          __self: true,
          __source: true
        };
        var specialPropKeyWarningShown, specialPropRefWarningShown, didWarnAboutStringRefs;
        {
          didWarnAboutStringRefs = {};
        }
        function hasValidRef(config) {
          {
            if (hasOwnProperty.call(config, "ref")) {
              var getter = Object.getOwnPropertyDescriptor(config, "ref").get;
              if (getter && getter.isReactWarning) {
                return false;
              }
            }
          }
          return config.ref !== void 0;
        }
        function hasValidKey(config) {
          {
            if (hasOwnProperty.call(config, "key")) {
              var getter = Object.getOwnPropertyDescriptor(config, "key").get;
              if (getter && getter.isReactWarning) {
                return false;
              }
            }
          }
          return config.key !== void 0;
        }
        function defineKeyPropWarningGetter(props, displayName) {
          var warnAboutAccessingKey = function() {
            {
              if (!specialPropKeyWarningShown) {
                specialPropKeyWarningShown = true;
                error("%s: `key` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://reactjs.org/link/special-props)", displayName);
              }
            }
          };
          warnAboutAccessingKey.isReactWarning = true;
          Object.defineProperty(props, "key", {
            get: warnAboutAccessingKey,
            configurable: true
          });
        }
        function defineRefPropWarningGetter(props, displayName) {
          var warnAboutAccessingRef = function() {
            {
              if (!specialPropRefWarningShown) {
                specialPropRefWarningShown = true;
                error("%s: `ref` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://reactjs.org/link/special-props)", displayName);
              }
            }
          };
          warnAboutAccessingRef.isReactWarning = true;
          Object.defineProperty(props, "ref", {
            get: warnAboutAccessingRef,
            configurable: true
          });
        }
        function warnIfStringRefCannotBeAutoConverted(config) {
          {
            if (typeof config.ref === "string" && ReactCurrentOwner.current && config.__self && ReactCurrentOwner.current.stateNode !== config.__self) {
              var componentName = getComponentNameFromType(ReactCurrentOwner.current.type);
              if (!didWarnAboutStringRefs[componentName]) {
                error('Component "%s" contains the string ref "%s". Support for string refs will be removed in a future major release. This case cannot be automatically converted to an arrow function. We ask you to manually fix this case by using useRef() or createRef() instead. Learn more about using refs safely here: https://reactjs.org/link/strict-mode-string-ref', componentName, config.ref);
                didWarnAboutStringRefs[componentName] = true;
              }
            }
          }
        }
        var ReactElement = function(type, key, ref, self, source, owner, props) {
          var element = {
            // This tag allows us to uniquely identify this as a React Element
            $$typeof: REACT_ELEMENT_TYPE,
            // Built-in properties that belong on the element
            type,
            key,
            ref,
            props,
            // Record the component responsible for creating this element.
            _owner: owner
          };
          {
            element._store = {};
            Object.defineProperty(element._store, "validated", {
              configurable: false,
              enumerable: false,
              writable: true,
              value: false
            });
            Object.defineProperty(element, "_self", {
              configurable: false,
              enumerable: false,
              writable: false,
              value: self
            });
            Object.defineProperty(element, "_source", {
              configurable: false,
              enumerable: false,
              writable: false,
              value: source
            });
            if (Object.freeze) {
              Object.freeze(element.props);
              Object.freeze(element);
            }
          }
          return element;
        };
        function createElement(type, config, children) {
          var propName;
          var props = {};
          var key = null;
          var ref = null;
          var self = null;
          var source = null;
          if (config != null) {
            if (hasValidRef(config)) {
              ref = config.ref;
              {
                warnIfStringRefCannotBeAutoConverted(config);
              }
            }
            if (hasValidKey(config)) {
              {
                checkKeyStringCoercion(config.key);
              }
              key = "" + config.key;
            }
            self = config.__self === void 0 ? null : config.__self;
            source = config.__source === void 0 ? null : config.__source;
            for (propName in config) {
              if (hasOwnProperty.call(config, propName) && !RESERVED_PROPS.hasOwnProperty(propName)) {
                props[propName] = config[propName];
              }
            }
          }
          var childrenLength = arguments.length - 2;
          if (childrenLength === 1) {
            props.children = children;
          } else if (childrenLength > 1) {
            var childArray = Array(childrenLength);
            for (var i = 0; i < childrenLength; i++) {
              childArray[i] = arguments[i + 2];
            }
            {
              if (Object.freeze) {
                Object.freeze(childArray);
              }
            }
            props.children = childArray;
          }
          if (type && type.defaultProps) {
            var defaultProps = type.defaultProps;
            for (propName in defaultProps) {
              if (props[propName] === void 0) {
                props[propName] = defaultProps[propName];
              }
            }
          }
          {
            if (key || ref) {
              var displayName = typeof type === "function" ? type.displayName || type.name || "Unknown" : type;
              if (key) {
                defineKeyPropWarningGetter(props, displayName);
              }
              if (ref) {
                defineRefPropWarningGetter(props, displayName);
              }
            }
          }
          return ReactElement(type, key, ref, self, source, ReactCurrentOwner.current, props);
        }
        function cloneAndReplaceKey(oldElement, newKey) {
          var newElement = ReactElement(oldElement.type, newKey, oldElement.ref, oldElement._self, oldElement._source, oldElement._owner, oldElement.props);
          return newElement;
        }
        function cloneElement(element, config, children) {
          if (element === null || element === void 0) {
            throw new Error("React.cloneElement(...): The argument must be a React element, but you passed " + element + ".");
          }
          var propName;
          var props = assign({}, element.props);
          var key = element.key;
          var ref = element.ref;
          var self = element._self;
          var source = element._source;
          var owner = element._owner;
          if (config != null) {
            if (hasValidRef(config)) {
              ref = config.ref;
              owner = ReactCurrentOwner.current;
            }
            if (hasValidKey(config)) {
              {
                checkKeyStringCoercion(config.key);
              }
              key = "" + config.key;
            }
            var defaultProps;
            if (element.type && element.type.defaultProps) {
              defaultProps = element.type.defaultProps;
            }
            for (propName in config) {
              if (hasOwnProperty.call(config, propName) && !RESERVED_PROPS.hasOwnProperty(propName)) {
                if (config[propName] === void 0 && defaultProps !== void 0) {
                  props[propName] = defaultProps[propName];
                } else {
                  props[propName] = config[propName];
                }
              }
            }
          }
          var childrenLength = arguments.length - 2;
          if (childrenLength === 1) {
            props.children = children;
          } else if (childrenLength > 1) {
            var childArray = Array(childrenLength);
            for (var i = 0; i < childrenLength; i++) {
              childArray[i] = arguments[i + 2];
            }
            props.children = childArray;
          }
          return ReactElement(element.type, key, ref, self, source, owner, props);
        }
        function isValidElement(object) {
          return typeof object === "object" && object !== null && object.$$typeof === REACT_ELEMENT_TYPE;
        }
        var SEPARATOR = ".";
        var SUBSEPARATOR = ":";
        function escape(key) {
          var escapeRegex = /[=:]/g;
          var escaperLookup = {
            "=": "=0",
            ":": "=2"
          };
          var escapedString = key.replace(escapeRegex, function(match) {
            return escaperLookup[match];
          });
          return "$" + escapedString;
        }
        var didWarnAboutMaps = false;
        var userProvidedKeyEscapeRegex = /\/+/g;
        function escapeUserProvidedKey(text) {
          return text.replace(userProvidedKeyEscapeRegex, "$&/");
        }
        function getElementKey(element, index) {
          if (typeof element === "object" && element !== null && element.key != null) {
            {
              checkKeyStringCoercion(element.key);
            }
            return escape("" + element.key);
          }
          return index.toString(36);
        }
        function mapIntoArray(children, array, escapedPrefix, nameSoFar, callback) {
          var type = typeof children;
          if (type === "undefined" || type === "boolean") {
            children = null;
          }
          var invokeCallback = false;
          if (children === null) {
            invokeCallback = true;
          } else {
            switch (type) {
              case "string":
              case "number":
                invokeCallback = true;
                break;
              case "object":
                switch (children.$$typeof) {
                  case REACT_ELEMENT_TYPE:
                  case REACT_PORTAL_TYPE:
                    invokeCallback = true;
                }
            }
          }
          if (invokeCallback) {
            var _child = children;
            var mappedChild = callback(_child);
            var childKey = nameSoFar === "" ? SEPARATOR + getElementKey(_child, 0) : nameSoFar;
            if (isArray(mappedChild)) {
              var escapedChildKey = "";
              if (childKey != null) {
                escapedChildKey = escapeUserProvidedKey(childKey) + "/";
              }
              mapIntoArray(mappedChild, array, escapedChildKey, "", function(c) {
                return c;
              });
            } else if (mappedChild != null) {
              if (isValidElement(mappedChild)) {
                {
                  if (mappedChild.key && (!_child || _child.key !== mappedChild.key)) {
                    checkKeyStringCoercion(mappedChild.key);
                  }
                }
                mappedChild = cloneAndReplaceKey(
                  mappedChild,
                  // Keep both the (mapped) and old keys if they differ, just as
                  // traverseAllChildren used to do for objects as children
                  escapedPrefix + // $FlowFixMe Flow incorrectly thinks React.Portal doesn't have a key
                  (mappedChild.key && (!_child || _child.key !== mappedChild.key) ? (
                    // $FlowFixMe Flow incorrectly thinks existing element's key can be a number
                    // eslint-disable-next-line react-internal/safe-string-coercion
                    escapeUserProvidedKey("" + mappedChild.key) + "/"
                  ) : "") + childKey
                );
              }
              array.push(mappedChild);
            }
            return 1;
          }
          var child;
          var nextName;
          var subtreeCount = 0;
          var nextNamePrefix = nameSoFar === "" ? SEPARATOR : nameSoFar + SUBSEPARATOR;
          if (isArray(children)) {
            for (var i = 0; i < children.length; i++) {
              child = children[i];
              nextName = nextNamePrefix + getElementKey(child, i);
              subtreeCount += mapIntoArray(child, array, escapedPrefix, nextName, callback);
            }
          } else {
            var iteratorFn = getIteratorFn(children);
            if (typeof iteratorFn === "function") {
              var iterableChildren = children;
              {
                if (iteratorFn === iterableChildren.entries) {
                  if (!didWarnAboutMaps) {
                    warn("Using Maps as children is not supported. Use an array of keyed ReactElements instead.");
                  }
                  didWarnAboutMaps = true;
                }
              }
              var iterator = iteratorFn.call(iterableChildren);
              var step;
              var ii = 0;
              while (!(step = iterator.next()).done) {
                child = step.value;
                nextName = nextNamePrefix + getElementKey(child, ii++);
                subtreeCount += mapIntoArray(child, array, escapedPrefix, nextName, callback);
              }
            } else if (type === "object") {
              var childrenString = String(children);
              throw new Error("Objects are not valid as a React child (found: " + (childrenString === "[object Object]" ? "object with keys {" + Object.keys(children).join(", ") + "}" : childrenString) + "). If you meant to render a collection of children, use an array instead.");
            }
          }
          return subtreeCount;
        }
        function mapChildren(children, func, context) {
          if (children == null) {
            return children;
          }
          var result = [];
          var count = 0;
          mapIntoArray(children, result, "", "", function(child) {
            return func.call(context, child, count++);
          });
          return result;
        }
        function countChildren(children) {
          var n = 0;
          mapChildren(children, function() {
            n++;
          });
          return n;
        }
        function forEachChildren(children, forEachFunc, forEachContext) {
          mapChildren(children, function() {
            forEachFunc.apply(this, arguments);
          }, forEachContext);
        }
        function toArray(children) {
          return mapChildren(children, function(child) {
            return child;
          }) || [];
        }
        function onlyChild(children) {
          if (!isValidElement(children)) {
            throw new Error("React.Children.only expected to receive a single React element child.");
          }
          return children;
        }
        function createContext(defaultValue) {
          var context = {
            $$typeof: REACT_CONTEXT_TYPE,
            // As a workaround to support multiple concurrent renderers, we categorize
            // some renderers as primary and others as secondary. We only expect
            // there to be two concurrent renderers at most: React Native (primary) and
            // Fabric (secondary); React DOM (primary) and React ART (secondary).
            // Secondary renderers store their context values on separate fields.
            _currentValue: defaultValue,
            _currentValue2: defaultValue,
            // Used to track how many concurrent renderers this context currently
            // supports within in a single renderer. Such as parallel server rendering.
            _threadCount: 0,
            // These are circular
            Provider: null,
            Consumer: null,
            // Add these to use same hidden class in VM as ServerContext
            _defaultValue: null,
            _globalName: null
          };
          context.Provider = {
            $$typeof: REACT_PROVIDER_TYPE,
            _context: context
          };
          var hasWarnedAboutUsingNestedContextConsumers = false;
          var hasWarnedAboutUsingConsumerProvider = false;
          var hasWarnedAboutDisplayNameOnConsumer = false;
          {
            var Consumer = {
              $$typeof: REACT_CONTEXT_TYPE,
              _context: context
            };
            Object.defineProperties(Consumer, {
              Provider: {
                get: function() {
                  if (!hasWarnedAboutUsingConsumerProvider) {
                    hasWarnedAboutUsingConsumerProvider = true;
                    error("Rendering <Context.Consumer.Provider> is not supported and will be removed in a future major release. Did you mean to render <Context.Provider> instead?");
                  }
                  return context.Provider;
                },
                set: function(_Provider) {
                  context.Provider = _Provider;
                }
              },
              _currentValue: {
                get: function() {
                  return context._currentValue;
                },
                set: function(_currentValue) {
                  context._currentValue = _currentValue;
                }
              },
              _currentValue2: {
                get: function() {
                  return context._currentValue2;
                },
                set: function(_currentValue2) {
                  context._currentValue2 = _currentValue2;
                }
              },
              _threadCount: {
                get: function() {
                  return context._threadCount;
                },
                set: function(_threadCount) {
                  context._threadCount = _threadCount;
                }
              },
              Consumer: {
                get: function() {
                  if (!hasWarnedAboutUsingNestedContextConsumers) {
                    hasWarnedAboutUsingNestedContextConsumers = true;
                    error("Rendering <Context.Consumer.Consumer> is not supported and will be removed in a future major release. Did you mean to render <Context.Consumer> instead?");
                  }
                  return context.Consumer;
                }
              },
              displayName: {
                get: function() {
                  return context.displayName;
                },
                set: function(displayName) {
                  if (!hasWarnedAboutDisplayNameOnConsumer) {
                    warn("Setting `displayName` on Context.Consumer has no effect. You should set it directly on the context with Context.displayName = '%s'.", displayName);
                    hasWarnedAboutDisplayNameOnConsumer = true;
                  }
                }
              }
            });
            context.Consumer = Consumer;
          }
          {
            context._currentRenderer = null;
            context._currentRenderer2 = null;
          }
          return context;
        }
        var Uninitialized = -1;
        var Pending = 0;
        var Resolved = 1;
        var Rejected = 2;
        function lazyInitializer(payload) {
          if (payload._status === Uninitialized) {
            var ctor = payload._result;
            var thenable = ctor();
            thenable.then(function(moduleObject2) {
              if (payload._status === Pending || payload._status === Uninitialized) {
                var resolved = payload;
                resolved._status = Resolved;
                resolved._result = moduleObject2;
              }
            }, function(error2) {
              if (payload._status === Pending || payload._status === Uninitialized) {
                var rejected = payload;
                rejected._status = Rejected;
                rejected._result = error2;
              }
            });
            if (payload._status === Uninitialized) {
              var pending = payload;
              pending._status = Pending;
              pending._result = thenable;
            }
          }
          if (payload._status === Resolved) {
            var moduleObject = payload._result;
            {
              if (moduleObject === void 0) {
                error("lazy: Expected the result of a dynamic import() call. Instead received: %s\n\nYour code should look like: \n  const MyComponent = lazy(() => import('./MyComponent'))\n\nDid you accidentally put curly braces around the import?", moduleObject);
              }
            }
            {
              if (!("default" in moduleObject)) {
                error("lazy: Expected the result of a dynamic import() call. Instead received: %s\n\nYour code should look like: \n  const MyComponent = lazy(() => import('./MyComponent'))", moduleObject);
              }
            }
            return moduleObject.default;
          } else {
            throw payload._result;
          }
        }
        function lazy(ctor) {
          var payload = {
            // We use these fields to store the result.
            _status: Uninitialized,
            _result: ctor
          };
          var lazyType = {
            $$typeof: REACT_LAZY_TYPE,
            _payload: payload,
            _init: lazyInitializer
          };
          {
            var defaultProps;
            var propTypes;
            Object.defineProperties(lazyType, {
              defaultProps: {
                configurable: true,
                get: function() {
                  return defaultProps;
                },
                set: function(newDefaultProps) {
                  error("React.lazy(...): It is not supported to assign `defaultProps` to a lazy component import. Either specify them where the component is defined, or create a wrapping component around it.");
                  defaultProps = newDefaultProps;
                  Object.defineProperty(lazyType, "defaultProps", {
                    enumerable: true
                  });
                }
              },
              propTypes: {
                configurable: true,
                get: function() {
                  return propTypes;
                },
                set: function(newPropTypes) {
                  error("React.lazy(...): It is not supported to assign `propTypes` to a lazy component import. Either specify them where the component is defined, or create a wrapping component around it.");
                  propTypes = newPropTypes;
                  Object.defineProperty(lazyType, "propTypes", {
                    enumerable: true
                  });
                }
              }
            });
          }
          return lazyType;
        }
        function forwardRef(render) {
          {
            if (render != null && render.$$typeof === REACT_MEMO_TYPE) {
              error("forwardRef requires a render function but received a `memo` component. Instead of forwardRef(memo(...)), use memo(forwardRef(...)).");
            } else if (typeof render !== "function") {
              error("forwardRef requires a render function but was given %s.", render === null ? "null" : typeof render);
            } else {
              if (render.length !== 0 && render.length !== 2) {
                error("forwardRef render functions accept exactly two parameters: props and ref. %s", render.length === 1 ? "Did you forget to use the ref parameter?" : "Any additional parameter will be undefined.");
              }
            }
            if (render != null) {
              if (render.defaultProps != null || render.propTypes != null) {
                error("forwardRef render functions do not support propTypes or defaultProps. Did you accidentally pass a React component?");
              }
            }
          }
          var elementType = {
            $$typeof: REACT_FORWARD_REF_TYPE,
            render
          };
          {
            var ownName;
            Object.defineProperty(elementType, "displayName", {
              enumerable: false,
              configurable: true,
              get: function() {
                return ownName;
              },
              set: function(name) {
                ownName = name;
                if (!render.name && !render.displayName) {
                  render.displayName = name;
                }
              }
            });
          }
          return elementType;
        }
        var REACT_MODULE_REFERENCE;
        {
          REACT_MODULE_REFERENCE = Symbol.for("react.module.reference");
        }
        function isValidElementType(type) {
          if (typeof type === "string" || typeof type === "function") {
            return true;
          }
          if (type === REACT_FRAGMENT_TYPE || type === REACT_PROFILER_TYPE || enableDebugTracing || type === REACT_STRICT_MODE_TYPE || type === REACT_SUSPENSE_TYPE || type === REACT_SUSPENSE_LIST_TYPE || enableLegacyHidden || type === REACT_OFFSCREEN_TYPE || enableScopeAPI || enableCacheElement || enableTransitionTracing) {
            return true;
          }
          if (typeof type === "object" && type !== null) {
            if (type.$$typeof === REACT_LAZY_TYPE || type.$$typeof === REACT_MEMO_TYPE || type.$$typeof === REACT_PROVIDER_TYPE || type.$$typeof === REACT_CONTEXT_TYPE || type.$$typeof === REACT_FORWARD_REF_TYPE || // This needs to include all possible module reference object
            // types supported by any Flight configuration anywhere since
            // we don't know which Flight build this will end up being used
            // with.
            type.$$typeof === REACT_MODULE_REFERENCE || type.getModuleId !== void 0) {
              return true;
            }
          }
          return false;
        }
        function memo(type, compare) {
          {
            if (!isValidElementType(type)) {
              error("memo: The first argument must be a component. Instead received: %s", type === null ? "null" : typeof type);
            }
          }
          var elementType = {
            $$typeof: REACT_MEMO_TYPE,
            type,
            compare: compare === void 0 ? null : compare
          };
          {
            var ownName;
            Object.defineProperty(elementType, "displayName", {
              enumerable: false,
              configurable: true,
              get: function() {
                return ownName;
              },
              set: function(name) {
                ownName = name;
                if (!type.name && !type.displayName) {
                  type.displayName = name;
                }
              }
            });
          }
          return elementType;
        }
        function resolveDispatcher() {
          var dispatcher = ReactCurrentDispatcher.current;
          {
            if (dispatcher === null) {
              error("Invalid hook call. Hooks can only be called inside of the body of a function component. This could happen for one of the following reasons:\n1. You might have mismatching versions of React and the renderer (such as React DOM)\n2. You might be breaking the Rules of Hooks\n3. You might have more than one copy of React in the same app\nSee https://reactjs.org/link/invalid-hook-call for tips about how to debug and fix this problem.");
            }
          }
          return dispatcher;
        }
        function useContext(Context) {
          var dispatcher = resolveDispatcher();
          {
            if (Context._context !== void 0) {
              var realContext = Context._context;
              if (realContext.Consumer === Context) {
                error("Calling useContext(Context.Consumer) is not supported, may cause bugs, and will be removed in a future major release. Did you mean to call useContext(Context) instead?");
              } else if (realContext.Provider === Context) {
                error("Calling useContext(Context.Provider) is not supported. Did you mean to call useContext(Context) instead?");
              }
            }
          }
          return dispatcher.useContext(Context);
        }
        function useState2(initialState) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useState(initialState);
        }
        function useReducer(reducer, initialArg, init) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useReducer(reducer, initialArg, init);
        }
        function useRef(initialValue) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useRef(initialValue);
        }
        function useEffect2(create, deps) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useEffect(create, deps);
        }
        function useInsertionEffect(create, deps) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useInsertionEffect(create, deps);
        }
        function useLayoutEffect(create, deps) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useLayoutEffect(create, deps);
        }
        function useCallback(callback, deps) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useCallback(callback, deps);
        }
        function useMemo(create, deps) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useMemo(create, deps);
        }
        function useImperativeHandle(ref, create, deps) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useImperativeHandle(ref, create, deps);
        }
        function useDebugValue(value, formatterFn) {
          {
            var dispatcher = resolveDispatcher();
            return dispatcher.useDebugValue(value, formatterFn);
          }
        }
        function useTransition() {
          var dispatcher = resolveDispatcher();
          return dispatcher.useTransition();
        }
        function useDeferredValue(value) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useDeferredValue(value);
        }
        function useId() {
          var dispatcher = resolveDispatcher();
          return dispatcher.useId();
        }
        function useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot) {
          var dispatcher = resolveDispatcher();
          return dispatcher.useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
        }
        var disabledDepth = 0;
        var prevLog;
        var prevInfo;
        var prevWarn;
        var prevError;
        var prevGroup;
        var prevGroupCollapsed;
        var prevGroupEnd;
        function disabledLog() {
        }
        disabledLog.__reactDisabledLog = true;
        function disableLogs() {
          {
            if (disabledDepth === 0) {
              prevLog = console.log;
              prevInfo = console.info;
              prevWarn = console.warn;
              prevError = console.error;
              prevGroup = console.group;
              prevGroupCollapsed = console.groupCollapsed;
              prevGroupEnd = console.groupEnd;
              var props = {
                configurable: true,
                enumerable: true,
                value: disabledLog,
                writable: true
              };
              Object.defineProperties(console, {
                info: props,
                log: props,
                warn: props,
                error: props,
                group: props,
                groupCollapsed: props,
                groupEnd: props
              });
            }
            disabledDepth++;
          }
        }
        function reenableLogs() {
          {
            disabledDepth--;
            if (disabledDepth === 0) {
              var props = {
                configurable: true,
                enumerable: true,
                writable: true
              };
              Object.defineProperties(console, {
                log: assign({}, props, {
                  value: prevLog
                }),
                info: assign({}, props, {
                  value: prevInfo
                }),
                warn: assign({}, props, {
                  value: prevWarn
                }),
                error: assign({}, props, {
                  value: prevError
                }),
                group: assign({}, props, {
                  value: prevGroup
                }),
                groupCollapsed: assign({}, props, {
                  value: prevGroupCollapsed
                }),
                groupEnd: assign({}, props, {
                  value: prevGroupEnd
                })
              });
            }
            if (disabledDepth < 0) {
              error("disabledDepth fell below zero. This is a bug in React. Please file an issue.");
            }
          }
        }
        var ReactCurrentDispatcher$1 = ReactSharedInternals.ReactCurrentDispatcher;
        var prefix;
        function describeBuiltInComponentFrame(name, source, ownerFn) {
          {
            if (prefix === void 0) {
              try {
                throw Error();
              } catch (x) {
                var match = x.stack.trim().match(/\n( *(at )?)/);
                prefix = match && match[1] || "";
              }
            }
            return "\n" + prefix + name;
          }
        }
        var reentry = false;
        var componentFrameCache;
        {
          var PossiblyWeakMap = typeof WeakMap === "function" ? WeakMap : Map;
          componentFrameCache = new PossiblyWeakMap();
        }
        function describeNativeComponentFrame(fn, construct) {
          if (!fn || reentry) {
            return "";
          }
          {
            var frame = componentFrameCache.get(fn);
            if (frame !== void 0) {
              return frame;
            }
          }
          var control;
          reentry = true;
          var previousPrepareStackTrace = Error.prepareStackTrace;
          Error.prepareStackTrace = void 0;
          var previousDispatcher;
          {
            previousDispatcher = ReactCurrentDispatcher$1.current;
            ReactCurrentDispatcher$1.current = null;
            disableLogs();
          }
          try {
            if (construct) {
              var Fake = function() {
                throw Error();
              };
              Object.defineProperty(Fake.prototype, "props", {
                set: function() {
                  throw Error();
                }
              });
              if (typeof Reflect === "object" && Reflect.construct) {
                try {
                  Reflect.construct(Fake, []);
                } catch (x) {
                  control = x;
                }
                Reflect.construct(fn, [], Fake);
              } else {
                try {
                  Fake.call();
                } catch (x) {
                  control = x;
                }
                fn.call(Fake.prototype);
              }
            } else {
              try {
                throw Error();
              } catch (x) {
                control = x;
              }
              fn();
            }
          } catch (sample) {
            if (sample && control && typeof sample.stack === "string") {
              var sampleLines = sample.stack.split("\n");
              var controlLines = control.stack.split("\n");
              var s = sampleLines.length - 1;
              var c = controlLines.length - 1;
              while (s >= 1 && c >= 0 && sampleLines[s] !== controlLines[c]) {
                c--;
              }
              for (; s >= 1 && c >= 0; s--, c--) {
                if (sampleLines[s] !== controlLines[c]) {
                  if (s !== 1 || c !== 1) {
                    do {
                      s--;
                      c--;
                      if (c < 0 || sampleLines[s] !== controlLines[c]) {
                        var _frame = "\n" + sampleLines[s].replace(" at new ", " at ");
                        if (fn.displayName && _frame.includes("<anonymous>")) {
                          _frame = _frame.replace("<anonymous>", fn.displayName);
                        }
                        {
                          if (typeof fn === "function") {
                            componentFrameCache.set(fn, _frame);
                          }
                        }
                        return _frame;
                      }
                    } while (s >= 1 && c >= 0);
                  }
                  break;
                }
              }
            }
          } finally {
            reentry = false;
            {
              ReactCurrentDispatcher$1.current = previousDispatcher;
              reenableLogs();
            }
            Error.prepareStackTrace = previousPrepareStackTrace;
          }
          var name = fn ? fn.displayName || fn.name : "";
          var syntheticFrame = name ? describeBuiltInComponentFrame(name) : "";
          {
            if (typeof fn === "function") {
              componentFrameCache.set(fn, syntheticFrame);
            }
          }
          return syntheticFrame;
        }
        function describeFunctionComponentFrame(fn, source, ownerFn) {
          {
            return describeNativeComponentFrame(fn, false);
          }
        }
        function shouldConstruct(Component2) {
          var prototype = Component2.prototype;
          return !!(prototype && prototype.isReactComponent);
        }
        function describeUnknownElementTypeFrameInDEV(type, source, ownerFn) {
          if (type == null) {
            return "";
          }
          if (typeof type === "function") {
            {
              return describeNativeComponentFrame(type, shouldConstruct(type));
            }
          }
          if (typeof type === "string") {
            return describeBuiltInComponentFrame(type);
          }
          switch (type) {
            case REACT_SUSPENSE_TYPE:
              return describeBuiltInComponentFrame("Suspense");
            case REACT_SUSPENSE_LIST_TYPE:
              return describeBuiltInComponentFrame("SuspenseList");
          }
          if (typeof type === "object") {
            switch (type.$$typeof) {
              case REACT_FORWARD_REF_TYPE:
                return describeFunctionComponentFrame(type.render);
              case REACT_MEMO_TYPE:
                return describeUnknownElementTypeFrameInDEV(type.type, source, ownerFn);
              case REACT_LAZY_TYPE: {
                var lazyComponent = type;
                var payload = lazyComponent._payload;
                var init = lazyComponent._init;
                try {
                  return describeUnknownElementTypeFrameInDEV(init(payload), source, ownerFn);
                } catch (x) {
                }
              }
            }
          }
          return "";
        }
        var loggedTypeFailures = {};
        var ReactDebugCurrentFrame$1 = ReactSharedInternals.ReactDebugCurrentFrame;
        function setCurrentlyValidatingElement(element) {
          {
            if (element) {
              var owner = element._owner;
              var stack = describeUnknownElementTypeFrameInDEV(element.type, element._source, owner ? owner.type : null);
              ReactDebugCurrentFrame$1.setExtraStackFrame(stack);
            } else {
              ReactDebugCurrentFrame$1.setExtraStackFrame(null);
            }
          }
        }
        function checkPropTypes(typeSpecs, values, location, componentName, element) {
          {
            var has = Function.call.bind(hasOwnProperty);
            for (var typeSpecName in typeSpecs) {
              if (has(typeSpecs, typeSpecName)) {
                var error$1 = void 0;
                try {
                  if (typeof typeSpecs[typeSpecName] !== "function") {
                    var err = Error((componentName || "React class") + ": " + location + " type `" + typeSpecName + "` is invalid; it must be a function, usually from the `prop-types` package, but received `" + typeof typeSpecs[typeSpecName] + "`.This often happens because of typos such as `PropTypes.function` instead of `PropTypes.func`.");
                    err.name = "Invariant Violation";
                    throw err;
                  }
                  error$1 = typeSpecs[typeSpecName](values, typeSpecName, componentName, location, null, "SECRET_DO_NOT_PASS_THIS_OR_YOU_WILL_BE_FIRED");
                } catch (ex) {
                  error$1 = ex;
                }
                if (error$1 && !(error$1 instanceof Error)) {
                  setCurrentlyValidatingElement(element);
                  error("%s: type specification of %s `%s` is invalid; the type checker function must return `null` or an `Error` but returned a %s. You may have forgotten to pass an argument to the type checker creator (arrayOf, instanceOf, objectOf, oneOf, oneOfType, and shape all require an argument).", componentName || "React class", location, typeSpecName, typeof error$1);
                  setCurrentlyValidatingElement(null);
                }
                if (error$1 instanceof Error && !(error$1.message in loggedTypeFailures)) {
                  loggedTypeFailures[error$1.message] = true;
                  setCurrentlyValidatingElement(element);
                  error("Failed %s type: %s", location, error$1.message);
                  setCurrentlyValidatingElement(null);
                }
              }
            }
          }
        }
        function setCurrentlyValidatingElement$1(element) {
          {
            if (element) {
              var owner = element._owner;
              var stack = describeUnknownElementTypeFrameInDEV(element.type, element._source, owner ? owner.type : null);
              setExtraStackFrame(stack);
            } else {
              setExtraStackFrame(null);
            }
          }
        }
        var propTypesMisspellWarningShown;
        {
          propTypesMisspellWarningShown = false;
        }
        function getDeclarationErrorAddendum() {
          if (ReactCurrentOwner.current) {
            var name = getComponentNameFromType(ReactCurrentOwner.current.type);
            if (name) {
              return "\n\nCheck the render method of `" + name + "`.";
            }
          }
          return "";
        }
        function getSourceInfoErrorAddendum(source) {
          if (source !== void 0) {
            var fileName = source.fileName.replace(/^.*[\\\/]/, "");
            var lineNumber = source.lineNumber;
            return "\n\nCheck your code at " + fileName + ":" + lineNumber + ".";
          }
          return "";
        }
        function getSourceInfoErrorAddendumForProps(elementProps) {
          if (elementProps !== null && elementProps !== void 0) {
            return getSourceInfoErrorAddendum(elementProps.__source);
          }
          return "";
        }
        var ownerHasKeyUseWarning = {};
        function getCurrentComponentErrorInfo(parentType) {
          var info = getDeclarationErrorAddendum();
          if (!info) {
            var parentName = typeof parentType === "string" ? parentType : parentType.displayName || parentType.name;
            if (parentName) {
              info = "\n\nCheck the top-level render call using <" + parentName + ">.";
            }
          }
          return info;
        }
        function validateExplicitKey(element, parentType) {
          if (!element._store || element._store.validated || element.key != null) {
            return;
          }
          element._store.validated = true;
          var currentComponentErrorInfo = getCurrentComponentErrorInfo(parentType);
          if (ownerHasKeyUseWarning[currentComponentErrorInfo]) {
            return;
          }
          ownerHasKeyUseWarning[currentComponentErrorInfo] = true;
          var childOwner = "";
          if (element && element._owner && element._owner !== ReactCurrentOwner.current) {
            childOwner = " It was passed a child from " + getComponentNameFromType(element._owner.type) + ".";
          }
          {
            setCurrentlyValidatingElement$1(element);
            error('Each child in a list should have a unique "key" prop.%s%s See https://reactjs.org/link/warning-keys for more information.', currentComponentErrorInfo, childOwner);
            setCurrentlyValidatingElement$1(null);
          }
        }
        function validateChildKeys(node, parentType) {
          if (typeof node !== "object") {
            return;
          }
          if (isArray(node)) {
            for (var i = 0; i < node.length; i++) {
              var child = node[i];
              if (isValidElement(child)) {
                validateExplicitKey(child, parentType);
              }
            }
          } else if (isValidElement(node)) {
            if (node._store) {
              node._store.validated = true;
            }
          } else if (node) {
            var iteratorFn = getIteratorFn(node);
            if (typeof iteratorFn === "function") {
              if (iteratorFn !== node.entries) {
                var iterator = iteratorFn.call(node);
                var step;
                while (!(step = iterator.next()).done) {
                  if (isValidElement(step.value)) {
                    validateExplicitKey(step.value, parentType);
                  }
                }
              }
            }
          }
        }
        function validatePropTypes(element) {
          {
            var type = element.type;
            if (type === null || type === void 0 || typeof type === "string") {
              return;
            }
            var propTypes;
            if (typeof type === "function") {
              propTypes = type.propTypes;
            } else if (typeof type === "object" && (type.$$typeof === REACT_FORWARD_REF_TYPE || // Note: Memo only checks outer props here.
            // Inner props are checked in the reconciler.
            type.$$typeof === REACT_MEMO_TYPE)) {
              propTypes = type.propTypes;
            } else {
              return;
            }
            if (propTypes) {
              var name = getComponentNameFromType(type);
              checkPropTypes(propTypes, element.props, "prop", name, element);
            } else if (type.PropTypes !== void 0 && !propTypesMisspellWarningShown) {
              propTypesMisspellWarningShown = true;
              var _name = getComponentNameFromType(type);
              error("Component %s declared `PropTypes` instead of `propTypes`. Did you misspell the property assignment?", _name || "Unknown");
            }
            if (typeof type.getDefaultProps === "function" && !type.getDefaultProps.isReactClassApproved) {
              error("getDefaultProps is only used on classic React.createClass definitions. Use a static property named `defaultProps` instead.");
            }
          }
        }
        function validateFragmentProps(fragment) {
          {
            var keys = Object.keys(fragment.props);
            for (var i = 0; i < keys.length; i++) {
              var key = keys[i];
              if (key !== "children" && key !== "key") {
                setCurrentlyValidatingElement$1(fragment);
                error("Invalid prop `%s` supplied to `React.Fragment`. React.Fragment can only have `key` and `children` props.", key);
                setCurrentlyValidatingElement$1(null);
                break;
              }
            }
            if (fragment.ref !== null) {
              setCurrentlyValidatingElement$1(fragment);
              error("Invalid attribute `ref` supplied to `React.Fragment`.");
              setCurrentlyValidatingElement$1(null);
            }
          }
        }
        function createElementWithValidation(type, props, children) {
          var validType = isValidElementType(type);
          if (!validType) {
            var info = "";
            if (type === void 0 || typeof type === "object" && type !== null && Object.keys(type).length === 0) {
              info += " You likely forgot to export your component from the file it's defined in, or you might have mixed up default and named imports.";
            }
            var sourceInfo = getSourceInfoErrorAddendumForProps(props);
            if (sourceInfo) {
              info += sourceInfo;
            } else {
              info += getDeclarationErrorAddendum();
            }
            var typeString;
            if (type === null) {
              typeString = "null";
            } else if (isArray(type)) {
              typeString = "array";
            } else if (type !== void 0 && type.$$typeof === REACT_ELEMENT_TYPE) {
              typeString = "<" + (getComponentNameFromType(type.type) || "Unknown") + " />";
              info = " Did you accidentally export a JSX literal instead of a component?";
            } else {
              typeString = typeof type;
            }
            {
              error("React.createElement: type is invalid -- expected a string (for built-in components) or a class/function (for composite components) but got: %s.%s", typeString, info);
            }
          }
          var element = createElement.apply(this, arguments);
          if (element == null) {
            return element;
          }
          if (validType) {
            for (var i = 2; i < arguments.length; i++) {
              validateChildKeys(arguments[i], type);
            }
          }
          if (type === REACT_FRAGMENT_TYPE) {
            validateFragmentProps(element);
          } else {
            validatePropTypes(element);
          }
          return element;
        }
        var didWarnAboutDeprecatedCreateFactory = false;
        function createFactoryWithValidation(type) {
          var validatedFactory = createElementWithValidation.bind(null, type);
          validatedFactory.type = type;
          {
            if (!didWarnAboutDeprecatedCreateFactory) {
              didWarnAboutDeprecatedCreateFactory = true;
              warn("React.createFactory() is deprecated and will be removed in a future major release. Consider using JSX or use React.createElement() directly instead.");
            }
            Object.defineProperty(validatedFactory, "type", {
              enumerable: false,
              get: function() {
                warn("Factory.type is deprecated. Access the class directly before passing it to createFactory.");
                Object.defineProperty(this, "type", {
                  value: type
                });
                return type;
              }
            });
          }
          return validatedFactory;
        }
        function cloneElementWithValidation(element, props, children) {
          var newElement = cloneElement.apply(this, arguments);
          for (var i = 2; i < arguments.length; i++) {
            validateChildKeys(arguments[i], newElement.type);
          }
          validatePropTypes(newElement);
          return newElement;
        }
        function startTransition(scope, options) {
          var prevTransition = ReactCurrentBatchConfig.transition;
          ReactCurrentBatchConfig.transition = {};
          var currentTransition = ReactCurrentBatchConfig.transition;
          {
            ReactCurrentBatchConfig.transition._updatedFibers = /* @__PURE__ */ new Set();
          }
          try {
            scope();
          } finally {
            ReactCurrentBatchConfig.transition = prevTransition;
            {
              if (prevTransition === null && currentTransition._updatedFibers) {
                var updatedFibersCount = currentTransition._updatedFibers.size;
                if (updatedFibersCount > 10) {
                  warn("Detected a large number of updates inside startTransition. If this is due to a subscription please re-write it to use React provided hooks. Otherwise concurrent mode guarantees are off the table.");
                }
                currentTransition._updatedFibers.clear();
              }
            }
          }
        }
        var didWarnAboutMessageChannel = false;
        var enqueueTaskImpl = null;
        function enqueueTask(task) {
          if (enqueueTaskImpl === null) {
            try {
              var requireString = ("require" + Math.random()).slice(0, 7);
              var nodeRequire = module && module[requireString];
              enqueueTaskImpl = nodeRequire.call(module, "timers").setImmediate;
            } catch (_err) {
              enqueueTaskImpl = function(callback) {
                {
                  if (didWarnAboutMessageChannel === false) {
                    didWarnAboutMessageChannel = true;
                    if (typeof MessageChannel === "undefined") {
                      error("This browser does not have a MessageChannel implementation, so enqueuing tasks via await act(async () => ...) will fail. Please file an issue at https://github.com/facebook/react/issues if you encounter this warning.");
                    }
                  }
                }
                var channel = new MessageChannel();
                channel.port1.onmessage = callback;
                channel.port2.postMessage(void 0);
              };
            }
          }
          return enqueueTaskImpl(task);
        }
        var actScopeDepth = 0;
        var didWarnNoAwaitAct = false;
        function act(callback) {
          {
            var prevActScopeDepth = actScopeDepth;
            actScopeDepth++;
            if (ReactCurrentActQueue.current === null) {
              ReactCurrentActQueue.current = [];
            }
            var prevIsBatchingLegacy = ReactCurrentActQueue.isBatchingLegacy;
            var result;
            try {
              ReactCurrentActQueue.isBatchingLegacy = true;
              result = callback();
              if (!prevIsBatchingLegacy && ReactCurrentActQueue.didScheduleLegacyUpdate) {
                var queue = ReactCurrentActQueue.current;
                if (queue !== null) {
                  ReactCurrentActQueue.didScheduleLegacyUpdate = false;
                  flushActQueue(queue);
                }
              }
            } catch (error2) {
              popActScope(prevActScopeDepth);
              throw error2;
            } finally {
              ReactCurrentActQueue.isBatchingLegacy = prevIsBatchingLegacy;
            }
            if (result !== null && typeof result === "object" && typeof result.then === "function") {
              var thenableResult = result;
              var wasAwaited = false;
              var thenable = {
                then: function(resolve, reject) {
                  wasAwaited = true;
                  thenableResult.then(function(returnValue2) {
                    popActScope(prevActScopeDepth);
                    if (actScopeDepth === 0) {
                      recursivelyFlushAsyncActWork(returnValue2, resolve, reject);
                    } else {
                      resolve(returnValue2);
                    }
                  }, function(error2) {
                    popActScope(prevActScopeDepth);
                    reject(error2);
                  });
                }
              };
              {
                if (!didWarnNoAwaitAct && typeof Promise !== "undefined") {
                  Promise.resolve().then(function() {
                  }).then(function() {
                    if (!wasAwaited) {
                      didWarnNoAwaitAct = true;
                      error("You called act(async () => ...) without await. This could lead to unexpected testing behaviour, interleaving multiple act calls and mixing their scopes. You should - await act(async () => ...);");
                    }
                  });
                }
              }
              return thenable;
            } else {
              var returnValue = result;
              popActScope(prevActScopeDepth);
              if (actScopeDepth === 0) {
                var _queue = ReactCurrentActQueue.current;
                if (_queue !== null) {
                  flushActQueue(_queue);
                  ReactCurrentActQueue.current = null;
                }
                var _thenable = {
                  then: function(resolve, reject) {
                    if (ReactCurrentActQueue.current === null) {
                      ReactCurrentActQueue.current = [];
                      recursivelyFlushAsyncActWork(returnValue, resolve, reject);
                    } else {
                      resolve(returnValue);
                    }
                  }
                };
                return _thenable;
              } else {
                var _thenable2 = {
                  then: function(resolve, reject) {
                    resolve(returnValue);
                  }
                };
                return _thenable2;
              }
            }
          }
        }
        function popActScope(prevActScopeDepth) {
          {
            if (prevActScopeDepth !== actScopeDepth - 1) {
              error("You seem to have overlapping act() calls, this is not supported. Be sure to await previous act() calls before making a new one. ");
            }
            actScopeDepth = prevActScopeDepth;
          }
        }
        function recursivelyFlushAsyncActWork(returnValue, resolve, reject) {
          {
            var queue = ReactCurrentActQueue.current;
            if (queue !== null) {
              try {
                flushActQueue(queue);
                enqueueTask(function() {
                  if (queue.length === 0) {
                    ReactCurrentActQueue.current = null;
                    resolve(returnValue);
                  } else {
                    recursivelyFlushAsyncActWork(returnValue, resolve, reject);
                  }
                });
              } catch (error2) {
                reject(error2);
              }
            } else {
              resolve(returnValue);
            }
          }
        }
        var isFlushing = false;
        function flushActQueue(queue) {
          {
            if (!isFlushing) {
              isFlushing = true;
              var i = 0;
              try {
                for (; i < queue.length; i++) {
                  var callback = queue[i];
                  do {
                    callback = callback(true);
                  } while (callback !== null);
                }
                queue.length = 0;
              } catch (error2) {
                queue = queue.slice(i + 1);
                throw error2;
              } finally {
                isFlushing = false;
              }
            }
          }
        }
        var createElement$1 = createElementWithValidation;
        var cloneElement$1 = cloneElementWithValidation;
        var createFactory = createFactoryWithValidation;
        var Children = {
          map: mapChildren,
          forEach: forEachChildren,
          count: countChildren,
          toArray,
          only: onlyChild
        };
        exports.Children = Children;
        exports.Component = Component;
        exports.Fragment = REACT_FRAGMENT_TYPE;
        exports.Profiler = REACT_PROFILER_TYPE;
        exports.PureComponent = PureComponent;
        exports.StrictMode = REACT_STRICT_MODE_TYPE;
        exports.Suspense = REACT_SUSPENSE_TYPE;
        exports.__SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED = ReactSharedInternals;
        exports.act = act;
        exports.cloneElement = cloneElement$1;
        exports.createContext = createContext;
        exports.createElement = createElement$1;
        exports.createFactory = createFactory;
        exports.createRef = createRef;
        exports.forwardRef = forwardRef;
        exports.isValidElement = isValidElement;
        exports.lazy = lazy;
        exports.memo = memo;
        exports.startTransition = startTransition;
        exports.unstable_act = act;
        exports.useCallback = useCallback;
        exports.useContext = useContext;
        exports.useDebugValue = useDebugValue;
        exports.useDeferredValue = useDeferredValue;
        exports.useEffect = useEffect2;
        exports.useId = useId;
        exports.useImperativeHandle = useImperativeHandle;
        exports.useInsertionEffect = useInsertionEffect;
        exports.useLayoutEffect = useLayoutEffect;
        exports.useMemo = useMemo;
        exports.useReducer = useReducer;
        exports.useRef = useRef;
        exports.useState = useState2;
        exports.useSyncExternalStore = useSyncExternalStore;
        exports.useTransition = useTransition;
        exports.version = ReactVersion;
        if (typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ !== "undefined" && typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStop === "function") {
          __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStop(new Error());
        }
      })();
    }
  }
});

// node_modules/react/index.js
var require_react = __commonJS({
  "node_modules/react/index.js"(exports, module) {
    "use strict";
    if (process.env.NODE_ENV === "production") {
      module.exports = require_react_production_min();
    } else {
      module.exports = require_react_development();
    }
  }
});

// src/pages/Warehouse/StoreOrders/detailAuxiliaryLoads.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/hooks/useIsMobile.ts
var import_react = __toESM(require_react(), 1);
var MOBILE_BREAKPOINT = 767;
var PHONE_LANDSCAPE_MAX_HEIGHT = 500;
function resolveIsMobileViewport({ width, height, coarsePointer }) {
  if (width <= MOBILE_BREAKPOINT) {
    return true;
  }
  return coarsePointer && height <= PHONE_LANDSCAPE_MAX_HEIGHT;
}

// src/utils/detailLoadState.ts
function shouldShowDetailInitialLoading({
  requestedDetailId,
  loadedDetailId,
  visibleDetailId
}) {
  if (!requestedDetailId) {
    return false;
  }
  return loadedDetailId !== requestedDetailId || visibleDetailId !== requestedDetailId;
}
function shouldSkipDetailAutoReload({
  requestedDetailId,
  loadedDetailId,
  visibleDetailId,
  requestedDetailQueryKey,
  loadedDetailQueryKey
}) {
  if (!requestedDetailId) {
    return false;
  }
  const isSameDetail = loadedDetailId === requestedDetailId && visibleDetailId === requestedDetailId;
  if (!isSameDetail) {
    return false;
  }
  if (requestedDetailQueryKey !== void 0 || loadedDetailQueryKey !== void 0) {
    return requestedDetailQueryKey === loadedDetailQueryKey;
  }
  return true;
}

// src/pages/Warehouse/StoreOrders/detailLoadState.ts
function shouldLoadStoreOrderDetailPage({
  keepAliveActive,
  isMobileLayout
}) {
  return isMobileLayout || keepAliveActive;
}
function shouldShowStoreOrderDetailInitialLoading({
  requestedOrderId,
  loadedOrderId,
  visibleDetailId
}) {
  return shouldShowDetailInitialLoading({
    requestedDetailId: requestedOrderId,
    loadedDetailId: loadedOrderId,
    visibleDetailId
  });
}

// src/pages/Warehouse/StoreOrders/detailAuxiliaryLoads.logic.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
async function runTest(name, execute) {
  try {
    await execute();
    console.log(`ok - ${name}`);
    return null;
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`not ok - ${name}`);
    console.error(reason);
    return `${name}: ${reason}`;
  }
}
var detailFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/Detail.tsx");
var pickingListFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx");
var invoiceFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/Invoice.tsx");
var containerDetailFile = path.resolve(process.cwd(), "src/pages/Warehouse/ContainerDetail/index.tsx");
var localSupplierInvoiceDetailFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoiceDetailPage/index.tsx");
var localSupplierInvoiceEditFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/index.tsx");
var detailLoadStateFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/detailLoadState.ts");
var sharedDetailLoadStateFile = path.resolve(process.cwd(), "src/utils/detailLoadState.ts");
var zhFile = path.resolve(process.cwd(), "src/i18n/locales/zh.json");
var enFile = path.resolve(process.cwd(), "src/i18n/locales/en.json");
function readSource(file) {
  return readFileSync(file, "utf8").replace(/\r\n/g, "\n");
}
var detailSource = readSource(detailFile);
var pickingListSource = readSource(pickingListFile);
var invoiceSource = readSource(invoiceFile);
var containerDetailSource = readSource(containerDetailFile);
var localSupplierInvoiceDetailSource = readSource(localSupplierInvoiceDetailFile);
var localSupplierInvoiceEditSource = readSource(localSupplierInvoiceEditFile);
var detailLoadStateSource = readSource(detailLoadStateFile);
var sharedDetailLoadStateSource = readSource(sharedDetailLoadStateFile);
var zhSource = readSource(zhFile);
var enSource = readSource(enFile);
async function main() {
  const failures = [];
  const auxiliaryWarningFailure = await runTest("\u5206\u5E97\u4E0B\u62C9\u52A0\u8F7D\u5931\u8D25\u5E94\u964D\u7EA7\u4E3A\u975E\u963B\u65AD\u63D0\u793A", () => {
    assert(
      detailSource.includes("message.warning(t('storeOrders.detail.loadStoreOptionsFailed'"),
      "loadStores \u5931\u8D25\u65F6\u5E94\u4F7F\u7528\u975E\u963B\u65AD warning \u6587\u6848\uFF0C\u907F\u514D\u8BEF\u63D0\u793A\u6574\u5F20\u8BA2\u8D27\u660E\u7EC6\u5931\u8D25"
    );
    assert(
      !detailSource.includes("message.error(error instanceof Error ? error.message : t('storeOrders.loadStoresFailed'))"),
      "loadStores \u5931\u8D25\u65F6\u4E0D\u5E94\u76F4\u63A5\u900F\u4F20\u540E\u7AEF\u9519\u8BEF message"
    );
  });
  if (auxiliaryWarningFailure) failures.push(auxiliaryWarningFailure);
  const warehouseStaffStoreSelectorFailure = await runTest("\u4ED3\u5E93\u5458\u5DE5\u660E\u7EC6\u9875\u4E0D\u5E94\u8BF7\u6C42\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9", () => {
    assert(
      detailSource.includes("if (!canUseWarehouseManagerActions)") && detailSource.includes("setStores([])") && detailSource.includes("lastLoadedStoresQueryKeyRef.current = storesQueryKey") && detailSource.includes("return\n    }\n\n    setStoresLoading(true)"),
      "\u975E\u4ED3\u5E93\u7BA1\u7406\u5458\u5E94\u8DF3\u8FC7\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9\u63A5\u53E3\uFF0C\u907F\u514D WarehouseStaff \u56E0 /api/stores 403 \u770B\u5230\u5206\u5E97\u663E\u793A\u5931\u8D25"
    );
    assert(
      detailSource.includes("if (headerForm.storeCode && !options.some((item) => item.value === headerForm.storeCode))") && detailSource.includes("const currentStoreLabel = detail?.storeName") && detailSource.includes("`${detail.storeName} (${headerForm.storeCode})`") && detailSource.includes("`${headerForm.storeCode} (${t('column.currentStore')})`"),
      "\u5206\u5E97\u4E0B\u62C9\u8DF3\u8FC7\u540E\u5E94\u4F18\u5148\u4F7F\u7528\u660E\u7EC6\u63A5\u53E3\u8FD4\u56DE\u7684 storeName \u663E\u793A\u5F53\u524D\u8BA2\u5355\u5206\u5E97"
    );
    assert(
      !detailSource.includes("userGUID: canViewAllStores ? undefined : currentUser?.userGUID"),
      "\u8BE6\u60C5\u9875\u4E0D\u5E94\u7EE7\u7EED\u4E3A\u4ED3\u5E93\u5458\u5DE5\u8BF7\u6C42\u6309\u7528\u6237\u8FC7\u6EE4\u7684\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9"
    );
  });
  if (warehouseStaffStoreSelectorFailure) failures.push(warehouseStaffStoreSelectorFailure);
  const translationFailure = await runTest("\u5206\u5E97\u4E0B\u62C9\u975E\u963B\u65AD\u63D0\u793A\u5E94\u6709\u4E2D\u82F1\u6587\u6587\u6848", () => {
    assert(
      zhSource.includes('"loadStoreOptionsFailed": "\u5206\u5E97\u4E0B\u62C9\u52A0\u8F7D\u5931\u8D25\uFF0C\u8BA2\u5355\u660E\u7EC6\u53EF\u7EE7\u7EED\u67E5\u770B"'),
      "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u5206\u5E97\u4E0B\u62C9\u975E\u963B\u65AD\u63D0\u793A"
    );
    assert(
      enSource.includes('"loadStoreOptionsFailed": "Store selector failed to load. Order details remain available."'),
      "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u5206\u5E97\u4E0B\u62C9\u975E\u963B\u65AD\u63D0\u793A"
    );
  });
  if (translationFailure) failures.push(translationFailure);
  const detailSaveTranslationFailure = await runTest("\u8BA2\u8D27\u660E\u7EC6\u4FDD\u5B58\u548C\u91D1\u989D\u663E\u793A\u5E94\u6709\u4E2D\u82F1\u6587\u6587\u6848", () => {
    assert(zhSource.includes('"saveEditedLines": "\u6574\u5355\u4FDD\u5B58"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u6574\u5355\u4FDD\u5B58");
    assert(enSource.includes('"saveEditedLines": "Save All Lines"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u6574\u5355\u4FDD\u5B58");
    assert(zhSource.includes('"importPriceSyncConfirmTitle": "\u786E\u8BA4\u540C\u6B65\u8FDB\u53E3\u4EF7"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u6807\u9898");
    assert(enSource.includes('"importPriceSyncConfirmTitle": "Confirm Import Price Sync"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u6807\u9898");
    assert(
      zhSource.includes('"importPriceSyncConfirmContent": "\u8FDB\u53E3\u4EF7\u4FDD\u5B58\u540E\u4F1A\u540C\u6B65\u5199\u5165\u4ED3\u5E93\u5546\u54C1\u8868\u548C\u5206\u5E97\u8868\uFF0C\u8BF7\u786E\u8BA4\u662F\u5426\u7EE7\u7EED\u3002"'),
      "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u5185\u5BB9"
    );
    assert(
      enSource.includes('"importPriceSyncConfirmContent": "After saving, import prices will sync to warehouse products and store products. Continue?"'),
      "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u5185\u5BB9"
    );
    assert(zhSource.includes('"syncImportPriceCheckbox": "\u540C\u6B65\u8FDB\u53E3\u4EF7\u5230\u4ED3\u5E93\u5546\u54C1\u8868\u548C\u5206\u5E97\u8868"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u540C\u6B65\u8FDB\u53E3\u4EF7\u52FE\u9009\u9879");
    assert(enSource.includes('"syncImportPriceCheckbox": "Sync import price to warehouse products and store products"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u540C\u6B65\u8FDB\u53E3\u4EF7\u52FE\u9009\u9879");
    assert(zhSource.includes('"orderAmountLabel": "\u9884\u8BA1\u9500\u552E\u989D"'), "\u4E2D\u6587\u8BA2\u5355\u91D1\u989D\u6807\u7B7E\u5E94\u6539\u4E3A\u9884\u8BA1\u9500\u552E\u989D");
    assert(enSource.includes('"orderAmountLabel": "Estimated Sales"'), "\u82F1\u6587\u8BA2\u5355\u91D1\u989D\u6807\u7B7E\u5E94\u6539\u4E3A Estimated Sales");
    assert(zhSource.includes('"importAmountLabel": "\u53D1\u8D27\u91D1\u989D ex GST"'), "\u4E2D\u6587\u8FDB\u53E3\u91D1\u989D\u6807\u7B7E\u5E94\u6539\u4E3A\u53D1\u8D27\u91D1\u989D ex GST");
    assert(enSource.includes('"importAmountLabel": "Allocated Amount ex GST"'), "\u82F1\u6587\u8FDB\u53E3\u91D1\u989D\u6807\u7B7E\u5E94\u6539\u4E3A Allocated Amount ex GST");
    assert(zhSource.includes('"gstAmountLabel": "GST 10%"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11 GST 10%");
    assert(enSource.includes('"gstAmountLabel": "GST 10%"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11 GST 10%");
  });
  if (detailSaveTranslationFailure) failures.push(detailSaveTranslationFailure);
  const editabilityStateFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u590D\u7528\u8BA2\u5355\u72B6\u6001\u6743\u9650\u6D3E\u751F\u51FD\u6570", () => {
    assert(
      detailSource.includes("import { deriveStoreOrderDetailPermissions } from './storeOrderDetailPermissions'") && detailSource.includes("} = deriveStoreOrderDetailPermissions(detail?.flowStatus)"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u590D\u7528 deriveStoreOrderDetailPermissions \u6D3E\u751F\u72B6\u6001\u6743\u9650"
    );
  });
  if (editabilityStateFailure) failures.push(editabilityStateFailure);
  const editGuardFailure = await runTest("\u4E0D\u53EF\u7F16\u8F91\u8BA2\u5355\u7684\u5199\u5165\u53E3\u5E94\u5148\u8D70\u7EDF\u4E00 guard", () => {
    assert(
      detailSource.includes("function ensureOrderEditable") && detailSource.includes("message.warning(t('storeOrders.detail.orderReadonlyRefresh'))") && detailSource.includes("if (!ensureOrderEditable())") && detailSource.includes("handleSaveLine") && detailSource.includes("handleConfirmPaste"),
      "\u8BE6\u60C5\u9875\u5199\u64CD\u4F5C\u5C1A\u672A\u7EDF\u4E00\u62E6\u622A\u4E0D\u53EF\u7F16\u8F91\u8BA2\u5355"
    );
  });
  if (editGuardFailure) failures.push(editGuardFailure);
  const flowGuardFailure = await runTest("\u72B6\u6001\u6D41\u8F6C\u5199\u5165\u53E3\u5E94\u6709\u51FD\u6570\u5185\u4E8C\u6B21\u95E8\u7981", () => {
    assert(
      detailSource.includes("if (!canUseWarehouseManagerActions || !canStartPicking)") && detailSource.includes("if (!canUseWarehouseManagerActions || !canCompleteOrder)") && detailSource.includes("message.warning(t('storeOrders.detail.orderReadonlyRefresh'))"),
      "\u5F00\u59CB\u914D\u8D27/\u5B8C\u6210\u8BA2\u5355\u51FD\u6570\u5165\u53E3\u5C1A\u672A\u6309\u4ED3\u5E93\u7BA1\u7406\u5458\u6743\u9650\u548C\u72B6\u6001\u4E8C\u6B21\u62E6\u622A"
    );
  });
  if (flowGuardFailure) failures.push(flowGuardFailure);
  const completeOrderOutboundDateFailure = await runTest("\u8BE6\u60C5\u9875\u5B8C\u6210\u8BA2\u5355\u5E94\u53EA\u5728\u51FA\u5E93\u65E5\u671F\u4E3A\u7A7A\u65F6\u8865\u5F53\u5929", () => {
    const completeOrderSource = detailSource.slice(
      detailSource.indexOf("const handleCompleteOrder"),
      detailSource.indexOf("const handleChangeOrderStatus")
    );
    assert(detailSource.includes("function formatLocalDateForInput"), "\u8BE6\u60C5\u9875\u5E94\u63D0\u4F9B\u672C\u5730\u65E5\u671F\u683C\u5F0F\u5316 helper\uFF0C\u907F\u514D UTC \u65E5\u671F\u504F\u79FB");
    assert(!detailSource.includes("completeStoreOrder,"), "\u8BE6\u60C5\u9875\u5B8C\u6210\u8BA2\u5355\u4E0D\u5E94\u518D\u5BFC\u5165\u76F4\u63A5\u5B8C\u6210\u63A5\u53E3");
    assert(!completeOrderSource.includes("completeStoreOrder(detail.orderGUID)"), "\u8BE6\u60C5\u9875\u5B8C\u6210\u8BA2\u5355\u4E0D\u5E94\u76F4\u63A5\u8C03\u7528\u5B8C\u6210\u63A5\u53E3");
    assert(
      completeOrderSource.includes("const currentOutboundDate = headerForm.outboundDate?.slice(0, 10)") && completeOrderSource.includes("const nextOutboundDate = currentOutboundDate || formatLocalDateForInput()") && completeOrderSource.includes("updateStoreOrderOutboundDate({") && completeOrderSource.includes("outboundDate: nextOutboundDate") && completeOrderSource.includes("completeOrder: true"),
      "\u5B8C\u6210\u8BA2\u5355\u5E94\u590D\u7528\u51FA\u5E93\u65E5\u671F\u63A5\u53E3\uFF1A\u5DF2\u6709\u51FA\u5E93\u65E5\u671F\u5219\u4FDD\u7559\uFF0C\u7A7A\u51FA\u5E93\u65E5\u671F\u624D\u8865\u5F53\u5929\u5E76\u540C\u6B65\u5B8C\u6210\u8BA2\u5355"
    );
  });
  if (completeOrderOutboundDateFailure) failures.push(completeOrderOutboundDateFailure);
  const disabledUiFailure = await runTest("\u975E\u4ED3\u5E93\u7BA1\u7406\u5458\u5E94\u7981\u7528\u8868\u5934\u548C\u660E\u7EC6\u5199\u63A7\u4EF6\uFF0C\u4EC5\u4FDD\u7559 WarehouseStaff \u53EA\u8BFB\u914D\u8D27\u5355\u5165\u53E3", () => {
    const orderDetailSectionSource = detailSource.slice(
      detailSource.indexOf("title={t('storeOrders.orderDetailSection')}"),
      detailSource.indexOf('className="store-order-detail-filter-bar"')
    );
    const pickingButtonSource = orderDetailSectionSource.slice(
      orderDetailSectionSource.indexOf("icon={<PrinterOutlined />}"),
      orderDetailSectionSource.indexOf("t('storeOrders.pickingList')")
    );
    const pickingButtonPosition = orderDetailSectionSource.indexOf("t('storeOrders.pickingList')");
    const managerGuardPosition = orderDetailSectionSource.lastIndexOf("{canUseWarehouseManagerActions ? (", pickingButtonPosition);
    const managerGuardClosePosition = orderDetailSectionSource.lastIndexOf(") : null}", pickingButtonPosition);
    assert(
      detailSource.includes("disabled={!canUseWarehouseManagerActions || isReadonlyOrder}") && detailSource.includes("disabled={!canUseWarehouseManagerActions || isReadonlyOrder || validPastePreviewCount === 0}") && detailSource.includes("disabled={isReadonlyOrder || !canStartPicking}") && detailSource.includes("disabled={!canCompleteOrder}") && detailSource.includes("extra={\n                  canUseWarehouseManagerActions ? (") && detailSource.includes("const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly") && detailSource.includes("const canUseStoreOrderDocumentActions = access.isWarehouseStaff") && detailSource.includes("const canUseStoreOrderDetailExtraActions = canUseWarehouseManagerActions || canUseStoreOrderDocumentActions") && orderDetailSectionSource.includes("canUseStoreOrderDetailExtraActions ? (\n                  <Space wrap>") && pickingButtonSource.includes("navigate(`/warehouse/store-order/picking/${detail.orderGUID}`)") && managerGuardPosition <= managerGuardClosePosition && detailSource.includes("rowSelection={\n                  canUseWarehouseManagerActions"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u6309\u4ED3\u5E93\u7BA1\u7406\u5458\u6743\u9650\u7981\u7528\u5199\u63A7\u4EF6\uFF0C\u6216\u672A\u4E3A WarehouseStaff \u4FDD\u7559\u53EA\u8BFB\u914D\u8D27\u5355\u5165\u53E3"
    );
  });
  if (disabledUiFailure) failures.push(disabledUiFailure);
  const statusChangeFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u63D0\u4F9B\u4E09\u72B6\u6001\u8BA2\u5355\u72B6\u6001\u66F4\u6539\u5165\u53E3", () => {
    assert(
      detailSource.includes("updateStoreOrderStatus") && detailSource.includes("handleChangeOrderStatus") && detailSource.includes("orderStatusChangeOptions") && detailSource.includes("StoreOrderFlowStatus.Submitted") && detailSource.includes("StoreOrderFlowStatus.Picking") && detailSource.includes("StoreOrderFlowStatus.Completed") && detailSource.includes("t('storeOrders.detail.changeOrderStatus'") && detailSource.includes("t('storeOrders.detail.statusChangeSuccess'"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u63D0\u4F9B\u4E09\u72B6\u6001\u8BA2\u5355\u72B6\u6001\u66F4\u6539\u5165\u53E3"
    );
  });
  if (statusChangeFailure) failures.push(statusChangeFailure);
  const keepAliveSkipAutoReloadFailure = await runTest("\u8BE6\u60C5\u9875 Tab \u5207\u56DE\u5DF2\u6709\u6570\u636E\u65F6\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0", () => {
    assert(
      detailSource.includes("loadedDetailIdRef") && detailSource.includes("useKeepAliveContext") && detailSource.includes("const { active } = useKeepAliveContext()") && detailSource.includes("import { useIsMobile } from '../../../hooks/useIsMobile'") && detailSource.includes("const isMobileLayout = useIsMobile()") && detailSource.includes("const canLoadDetail = shouldLoadStoreOrderDetailPage({") && detailSource.includes("if (!canLoadDetail) return") && detailSource.includes("visibleDetailIdRef") && detailSource.includes("lastLoadedDetailQueryKeyRef") && detailSource.includes("shouldSkipDetailAutoReload({") && detailSource.includes("shouldShowStoreOrderDetailInitialLoading({") && detailSource.includes("canLoadDetail,") && detailSource.includes("return () => {") && detailSource.includes("detailRequestControllerRef.current?.abort()"),
      "\u8BE6\u60C5\u9875\u7F3A\u5C11\u79FB\u52A8\u5E03\u5C40\u52A0\u8F7D\u95E8\u7981\u6216\u684C\u9762 KeepAlive active \u5B88\u536B"
    );
    assert(
      detailSource.includes("loadedDetailIdRef.current = result.orderGUID || id") && detailSource.includes("visibleDetailIdRef.current = result.orderGUID || id") && detailSource.includes("lastLoadedDetailQueryKeyRef.current = detailQueryKey"),
      "\u8BE6\u60C5\u9875\u52A0\u8F7D\u6210\u529F\u540E\u5E94\u8BB0\u5F55\u5DF2\u52A0\u8F7D\u8BA2\u5355 id \u548C\u67E5\u8BE2\u53C2\u6570\uFF0C\u540E\u7EED\u540C\u8BA2\u5355\u540C\u67E5\u8BE2\u624D\u80FD\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0"
    );
  });
  if (keepAliveSkipAutoReloadFailure) failures.push(keepAliveSkipAutoReloadFailure);
  const mobileDetailLoadGateFailure = await runTest("390px \u79FB\u52A8\u5E03\u5C40\u65E0 KeepAlive Provider \u65F6\u4E5F\u5E94\u52A0\u8F7D\u8BA2\u8D27\u660E\u7EC6", () => {
    const isMobileLayout = resolveIsMobileViewport({
      width: 390,
      height: 844,
      coarsePointer: true
    });
    assert(
      shouldLoadStoreOrderDetailPage({
        keepAliveActive: false,
        isMobileLayout
      }),
      "\u79FB\u52A8\u5E03\u5C40\u76F4\u63A5\u6E32\u67D3\u9875\u9762\u65F6\u5E94\u5FFD\u7565 KeepAlive \u9ED8\u8BA4 active=false\uFF0C\u907F\u514D\u6C38\u4E45\u505C\u5728 idle Spin"
    );
    assert(
      shouldLoadStoreOrderDetailPage({
        keepAliveActive: true,
        isMobileLayout: false
      }),
      "\u684C\u9762\u5F53\u524D\u6FC0\u6D3B Tab \u5E94\u7EE7\u7EED\u52A0\u8F7D\u8BA2\u8D27\u660E\u7EC6"
    );
    assert(
      !shouldLoadStoreOrderDetailPage({
        keepAliveActive: false,
        isMobileLayout: false
      }),
      "\u684C\u9762\u9690\u85CF KeepAlive Tab \u4ECD\u5E94\u963B\u6B62\u8BE6\u60C5\u8BF7\u6C42"
    );
  });
  if (mobileDetailLoadGateFailure) failures.push(mobileDetailLoadGateFailure);
  const initialLoadingDecisionFailure = await runTest("\u8BE6\u60C5\u9875\u521D\u59CB\u52A0\u8F7D\u548C\u81EA\u52A8\u5237\u65B0\u8DF3\u8FC7\u5224\u65AD\u5E94\u8986\u76D6\u5207\u56DE\u548C\u6362\u5355\u8FB9\u754C", () => {
    assert(
      sharedDetailLoadStateSource.includes("loadedDetailId !== requestedDetailId || visibleDetailId !== requestedDetailId") && sharedDetailLoadStateSource.includes("export function shouldSkipDetailAutoReload") && detailLoadStateSource.includes("shouldShowDetailInitialLoading"),
      "\u521D\u59CB\u52A0\u8F7D\u548C\u81EA\u52A8\u5237\u65B0\u8DF3\u8FC7\u5224\u65AD\u5E94\u6C89\u5230\u901A\u7528 helper\uFF0C\u5E76\u540C\u65F6\u68C0\u67E5\u5DF2\u52A0\u8F7D\u8BB0\u5F55\u548C\u5F53\u524D\u53EF\u5C55\u793A\u8BB0\u5F55"
    );
    assert(
      !shouldShowDetailInitialLoading({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a"
      }) && !shouldShowStoreOrderDetailInitialLoading({
        requestedOrderId: "order-a",
        loadedOrderId: "order-a",
        visibleDetailId: "order-a"
      }),
      "\u540C\u8BA2\u5355\u4E14\u5F53\u524D\u4ECD\u6709\u53EF\u5C55\u793A\u660E\u7EC6\u65F6\u5E94\u9759\u9ED8\u5237\u65B0"
    );
    assert(
      shouldSkipDetailAutoReload({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a"
      }),
      "\u540C\u8BE6\u60C5\u4E14\u5F53\u524D\u4ECD\u6709\u53EF\u5C55\u793A\u5185\u5BB9\u65F6\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0"
    );
    assert(
      shouldShowStoreOrderDetailInitialLoading({
        requestedOrderId: "order-b",
        loadedOrderId: "order-a",
        visibleDetailId: "order-a"
      }),
      "\u5207\u5230\u65B0\u8BA2\u5355\u65F6\u5E94\u663E\u793A\u9996\u6B21\u4E3B\u52A0\u8F7D"
    );
    assert(
      shouldShowStoreOrderDetailInitialLoading({
        requestedOrderId: "order-a",
        loadedOrderId: "order-a",
        visibleDetailId: null
      }),
      "\u5F53\u524D\u6CA1\u6709\u53EF\u5C55\u793A\u660E\u7EC6\u65F6\u5373\u4F7F\u5DF2\u52A0\u8F7D\u6807\u8BB0\u547D\u4E2D\u4E5F\u5E94\u663E\u793A\u4E3B\u52A0\u8F7D"
    );
    assert(
      shouldShowStoreOrderDetailInitialLoading({
        requestedOrderId: "order-a",
        loadedOrderId: "order-a",
        visibleDetailId: "order-b"
      }),
      "\u5F53\u524D\u53EF\u5C55\u793A\u660E\u7EC6\u5C5E\u4E8E\u5176\u4ED6\u8BA2\u5355\u65F6\u5E94\u663E\u793A\u4E3B\u52A0\u8F7D\uFF0C\u907F\u514D\u77ED\u6682\u5C55\u793A\u9519\u8BEF\u8BA2\u5355\u72B6\u6001"
    );
    assert(
      !shouldSkipDetailAutoReload({
        requestedDetailId: "detail-b",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a"
      }) && !shouldSkipDetailAutoReload({
        requestedDetailId: "",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a"
      }) && !shouldSkipDetailAutoReload({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: null
      }),
      "\u6362\u8BE6\u60C5\u3001\u7A7A id \u6216\u6CA1\u6709\u53EF\u5C55\u793A\u5185\u5BB9\u65F6\u4E0D\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0"
    );
    assert(
      shouldSkipDetailAutoReload({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a",
        requestedDetailQueryKey: '{"pageNumber":1}',
        loadedDetailQueryKey: '{"pageNumber":1}'
      }) && !shouldSkipDetailAutoReload({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a",
        requestedDetailQueryKey: '{"pageNumber":2}',
        loadedDetailQueryKey: '{"pageNumber":1}'
      }),
      "\u95E8\u5E97\u8BA2\u5355\u8BE6\u60C5\u67E5\u8BE2\u53C2\u6570\u4E00\u81F4\u624D\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\uFF0C\u5206\u9875\u641C\u7D22\u6392\u5E8F\u53D8\u5316\u5FC5\u987B\u91CD\u65B0\u8BF7\u6C42"
    );
  });
  if (initialLoadingDecisionFailure) failures.push(initialLoadingDecisionFailure);
  const silentFailurePreserveFailure = await runTest("\u8BE6\u60C5\u9875\u9759\u9ED8\u5237\u65B0\u5931\u8D25\u4E0D\u5E94\u6E05\u7A7A\u5F53\u524D\u660E\u7EC6", () => {
    assert(
      detailSource.includes("const errorMessage = error instanceof Error ? error.message : t('storeOrders.detail.loadDetailFailed')") && detailSource.includes("if (showLoading)") && detailSource.includes("setDetail(null)") && detailSource.includes("setDetailLoadStatus('error')") && detailSource.includes("setDetailErrorMessage(errorMessage)") && detailSource.includes("message.error(errorMessage)"),
      "\u9759\u9ED8\u5237\u65B0\u5931\u8D25\u65F6\u5E94\u4FDD\u7559\u65E7 detail\uFF0C\u53EA\u63D0\u793A\u9519\u8BEF\uFF1B\u9996\u6B21\u52A0\u8F7D\u5931\u8D25\u624D\u8FDB\u5165 error \u7A7A\u6001"
    );
  });
  if (silentFailurePreserveFailure) failures.push(silentFailurePreserveFailure);
  const storeOrderPrintPagesKeepAliveFailure = await runTest("\u914D\u8D27\u5355\u548C\u53D1\u7968 Tab \u5207\u56DE\u5DF2\u6709\u6570\u636E\u65F6\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0", () => {
    for (const [pageName, source, loadFailureKey] of [
      ["\u914D\u8D27\u5355", pickingListSource, "warehouse.pickingList.loadFailed"],
      ["\u53D1\u7968", invoiceSource, "warehouse.invoice.loadFailed"]
    ]) {
      assert(
        source.includes("import { shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'") && source.includes("loadedOrderIdRef") && source.includes("visibleOrderIdRef") && source.includes("const load = async (showLoading = true)") && source.includes("if (showLoading) {") && source.includes("setLoading(true)") && source.includes("shouldSkipDetailAutoReload({") && source.includes("return"),
        `${pageName}\u7F3A\u5C11\u540C\u8BA2\u5355 Tab \u6062\u590D\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\u4FDD\u62A4`
      );
      assert(
        source.includes("loadedOrderIdRef.current = detail.orderGUID || id") && source.includes("visibleOrderIdRef.current = detail.orderGUID || id") && source.includes(`const errorMessage = error instanceof Error ? error.message : t('${loadFailureKey}')`) && source.includes("if (showLoading) {") && source.includes("setOrder(null)") && source.includes("setStore(null)"),
        `${pageName}\u5E94\u5728\u6210\u529F\u540E\u8BB0\u5F55\u53EF\u5C55\u793A\u8BA2\u5355\uFF0C\u4E14\u9996\u6B21\u52A0\u8F7D\u5931\u8D25\u624D\u6E05\u7A7A\u5F53\u524D\u5185\u5BB9`
      );
    }
    assert(
      pickingListSource.includes("import { useKeepAliveContext } from 'keepalive-for-react'") && pickingListSource.includes("const { active } = useKeepAliveContext()") && pickingListSource.includes("const activeRef = useRef(active)") && pickingListSource.includes("activeRef.current = active") && pickingListSource.includes("if (!active) return") && pickingListSource.includes("let cancelled = false") && pickingListSource.includes("if (cancelled || !activeRef.current) return") && pickingListSource.includes("cancelled = true") && pickingListSource.includes("}, [active, id])"),
      "\u914D\u8D27\u5355\u9875\u7F3A\u5C11 KeepAlive active/\u5F02\u6B65\u7ED3\u679C\u5B88\u536B\uFF0C\u9690\u85CF Tab \u4E0D\u5E94\u8DDF\u968F\u5168\u5C40\u8DEF\u7531\u52A0\u8F7D\u6216\u7EE7\u7EED\u6253\u5370"
    );
  });
  if (storeOrderPrintPagesKeepAliveFailure) failures.push(storeOrderPrintPagesKeepAliveFailure);
  const warehouseStaffPickingStoreLoadFailure = await runTest("\u914D\u8D27\u5355\u9875 WarehouseStaff \u4E0D\u5E94\u8BF7\u6C42\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9", () => {
    assert(
      pickingListSource.includes("import { useAuthStore } from '../../../store/auth'") && pickingListSource.includes("const { access } = useAuthStore()") && pickingListSource.includes("const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly") && pickingListSource.includes("if (detail.storeCode && canUseWarehouseManagerActions)") && pickingListSource.includes("WarehouseStaff \u65E0\u9700\u52A0\u8F7D\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9") && pickingListSource.includes("store?.storeName || order.storeName || order.storeCode"),
      "\u914D\u8D27\u5355\u9875\u5E94\u8DF3\u8FC7 WarehouseStaff \u7684\u5B8C\u6574\u5206\u5E97\u63A5\u53E3\u8BF7\u6C42\uFF0C\u5E76\u4F7F\u7528\u8BA2\u5355\u8BE6\u60C5\u4E2D\u7684 storeName/storeCode \u5C55\u793A"
    );
  });
  if (warehouseStaffPickingStoreLoadFailure) failures.push(warehouseStaffPickingStoreLoadFailure);
  const warehouseStaffPickingPrintFailure = await runTest("\u914D\u8D27\u5355\u9875 WarehouseStaff \u6253\u5370\u4E0B\u8F7D\u4E0D\u5E94\u89E6\u53D1\u72B6\u6001\u6D41\u8F6C\u5199\u63A5\u53E3", () => {
    const beforePrintSource = pickingListSource.slice(
      pickingListSource.indexOf("const handleBeforePrint = async () => {"),
      pickingListSource.indexOf("const handlePrint = async () => {")
    );
    assert(
      beforePrintSource.includes("WarehouseStaff \u53EF\u6253\u5370/\u4E0B\u8F7D\u914D\u8D27\u5355") && beforePrintSource.includes("if (canUseWarehouseManagerActions && order.flowStatus === StoreOrderFlowStatus.Submitted)") && beforePrintSource.includes("await startPickingStoreOrder(order.orderGUID)") && beforePrintSource.includes("if (!activeRef.current)") && beforePrintSource.includes("loadedOrderIdRef.current = null") && pickingListSource.includes("await handleBeforePrint()") && pickingListSource.includes("await printElementPagesAsPdf") && pickingListSource.includes("await downloadElementPagesAsPdf"),
      "\u914D\u8D27\u5355\u6253\u5370/\u4E0B\u8F7D\u524D\u53EA\u6709\u4ED3\u5E93\u7BA1\u7406\u5458\u53EF\u81EA\u52A8\u5F00\u59CB\u914D\u8D27\uFF0C\u4E14\u5207\u6362 Tab \u540E\u4E0D\u80FD\u7EE7\u7EED\u751F\u6210\u65E7\u9875\u9762 PDF"
    );
  });
  if (warehouseStaffPickingPrintFailure) failures.push(warehouseStaffPickingPrintFailure);
  const lowRiskDetailPagesKeepAliveFailure = await runTest("\u4F4E\u98CE\u9669\u8BE6\u60C5\u9875 Tab \u5207\u56DE\u5E94\u4FDD\u7559\u5DF2\u6709\u5185\u5BB9\u5E76\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0", () => {
    assert(
      containerDetailSource.includes("import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'") && containerDetailSource.includes("useKeepAliveContext") && containerDetailSource.includes("const { active } = useKeepAliveContext()") && containerDetailSource.includes("if (!active) return") && containerDetailSource.includes("loadedContainerGuidRef") && containerDetailSource.includes("visibleContainerGuidRef") && containerDetailSource.includes("lastLoadedContainerDetailSuccessRef") && containerDetailSource.includes("const loadData = async (showLoading = true)") && containerDetailSource.includes("shouldSkipDetailAutoReload({") && containerDetailSource.includes("const activeLoadQueryKey = detailQueryKey") && containerDetailSource.includes("requestedDetailQueryKey: activeLoadQueryKey") && containerDetailSource.includes("loadedDetailQueryKey: lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid") && containerDetailSource.includes("void loadHeader(shouldShowInitialLoading)") && containerDetailSource.includes("loadDetailChunk(1, 'reset')") && containerDetailSource.includes("loadedContainerGuidRef.current = containerGuid") && containerDetailSource.includes("visibleContainerGuidRef.current = containerGuid") && containerDetailSource.includes("lastLoadedContainerDetailSuccessRef.current = { containerGuid, queryKey: detailQueryKey }"),
      "\u8D27\u67DC\u8BE6\u60C5\u7F3A\u5C11 KeepAlive active \u5B88\u536B\u6216\u660E\u7EC6\u67E5\u8BE2\u6761\u4EF6\u7F13\u5B58\u4FDD\u62A4"
    );
    assert(
      containerDetailSource.includes("setDetailTableRenderKey((value) => value + 1)") && containerDetailSource.includes("detailTableRef.current?.scrollTo?.({ top: scrollTop })") && containerDetailSource.indexOf("setDetailTableRenderKey((value) => value + 1)") > containerDetailSource.indexOf("if (!active || wasActive || rows.length === 0)") && containerDetailSource.indexOf("loadDetailChunk(1, 'reset')") < containerDetailSource.indexOf("setDetailTableRenderKey((value) => value + 1)"),
      "\u8D27\u67DC\u660E\u7EC6 Tab \u5207\u56DE\u5DF2\u6709\u6570\u636E\u65F6\u5E94\u53EA\u6062\u590D\u865A\u62DF\u8868\u683C\u6D4B\u91CF\uFF0C\u4E0D\u80FD\u901A\u8FC7\u91CD\u65B0\u52A0\u8F7D\u660E\u7EC6\u4FEE\u590D\u7A7A\u767D"
    );
    assert(
      localSupplierInvoiceDetailSource.includes("import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'") && localSupplierInvoiceDetailSource.includes("loadedInvoiceGuidRef") && localSupplierInvoiceDetailSource.includes("visibleInvoiceGuidRef") && localSupplierInvoiceDetailSource.includes("const loadInvoice = async (showLoading = true)") && localSupplierInvoiceDetailSource.includes("shouldSkipDetailAutoReload({") && localSupplierInvoiceDetailSource.includes("loadedInvoiceGuidRef.current = invoiceGuid") && localSupplierInvoiceDetailSource.includes("visibleInvoiceGuidRef.current = invoiceGuid"),
      "\u672C\u5730\u4F9B\u5E94\u5546\u53D1\u7968\u53EA\u8BFB\u8BE6\u60C5\u7F3A\u5C11\u540C\u53D1\u7968 Tab \u6062\u590D\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\u4FDD\u62A4"
    );
    assert(
      localSupplierInvoiceEditSource.includes("import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../../utils/detailLoadState'") && localSupplierInvoiceEditSource.includes("loadedInvoiceGuidRef") && localSupplierInvoiceEditSource.includes("visibleInvoiceGuidRef") && localSupplierInvoiceEditSource.includes("const loadInvoice = useCallback(async (showLoading = true)") && localSupplierInvoiceEditSource.includes("shouldSkipDetailAutoReload({") && localSupplierInvoiceEditSource.includes("loadedInvoiceGuidRef.current = invoiceGuid") && localSupplierInvoiceEditSource.includes("visibleInvoiceGuidRef.current = invoiceGuid"),
      "\u672C\u5730\u4F9B\u5E94\u5546\u53D1\u7968\u7F16\u8F91\u9875\u7F3A\u5C11\u540C\u53D1\u7968 Tab \u6062\u590D\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\u4FDD\u62A4"
    );
  });
  if (lowRiskDetailPagesKeepAliveFailure) failures.push(lowRiskDetailPagesKeepAliveFailure);
  const readonlyCopyFailure = await runTest("\u53EA\u8BFB\u72B6\u6001\u5E94\u63D0\u4F9B\u4E2D\u82F1\u6587\u63D0\u793A\u6587\u6848", () => {
    assert(
      zhSource.includes('"orderReadonlyTitle": "\u5F53\u524D\u8BA2\u5355\u4E3A\u53EA\u8BFB\u72B6\u6001"') && zhSource.includes('"orderReadonlyDescription": "\u5DF2\u5B8C\u6210\u8BA2\u5355\u4E0D\u53EF\u7F16\u8F91\uFF0C\u8BF7\u66F4\u6539\u72B6\u6001\u540E\u518D\u64CD\u4F5C\u3002\u4F46\u4ECD\u53EF\u8865\u5F55\u6216\u4FEE\u6B63\u51FA\u5E93\u65E5\u671F\u3002"') && zhSource.includes('"orderReadonlyRefresh": "\u5F53\u524D\u8BA2\u5355\u72B6\u6001\u4E0D\u53EF\u7F16\u8F91\uFF0C\u8BF7\u5237\u65B0\u786E\u8BA4\u72B6\u6001\u3002"'),
      "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u8BA2\u5355\u53EA\u8BFB\u63D0\u793A"
    );
    assert(
      enSource.includes('"orderReadonlyTitle": "Order is read-only"') && enSource.includes('"orderReadonlyDescription": "Completed orders cannot be edited. Change the status before editing. The outbound date can still be corrected."') && enSource.includes('"orderReadonlyRefresh": "The current order status is not editable. Please refresh and confirm the status."'),
      "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u8BA2\u5355\u53EA\u8BFB\u63D0\u793A"
    );
  });
  if (readonlyCopyFailure) failures.push(readonlyCopyFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("detailAuxiliaryLoads.logic.test: ok");
}
await main();
/*! Bundled license information:

react/cjs/react.production.min.js:
  (**
   * @license React
   * react.production.min.js
   *
   * Copyright (c) Facebook, Inc. and its affiliates.
   *
   * This source code is licensed under the MIT license found in the
   * LICENSE file in the root directory of this source tree.
   *)

react/cjs/react.development.js:
  (**
   * @license React
   * react.development.js
   *
   * Copyright (c) Facebook, Inc. and its affiliates.
   *
   * This source code is licensed under the MIT license found in the
   * LICENSE file in the root directory of this source tree.
   *)
*/

'use strict';

const path = require('path');

const _isWindows = process.platform === 'win32';

/**
 * Encode a URI component, allowing certain characters through unencoded.
 * @param {string} value
 * @param {boolean} allowSlash - allow '/' unencoded
 * @param {boolean} allowColon - allow ':' unencoded
 * @returns {string}
 */
function _encodeURIComponent(value, allowSlash, allowColon) {
  let result = '';
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i);
    if (
      (code >= 0x61 && code <= 0x7A) || // a-z
      (code >= 0x41 && code <= 0x5A) || // A-Z
      (code >= 0x30 && code <= 0x39) || // 0-9
      code === 0x2D || // -
      code === 0x2E || // .
      code === 0x5F || // _
      code === 0x7E    // ~
    ) {
      result += value[i];
    } else if (code === 0x2F && allowSlash) {
      result += value[i];
    } else if (code === 0x3A && allowColon) {
      result += value[i];
    } else if (code < 0x80) {
      result += '%' + code.toString(16).toUpperCase().padStart(2, '0');
    } else if (code < 0x800) {
      result += '%' + ((0xC0 | ((code >> 6) & 0x1F)).toString(16).toUpperCase());
      result += '%' + ((0x80 | (code & 0x3F)).toString(16).toUpperCase());
    } else {
      result += '%' + ((0xE0 | ((code >> 12) & 0x0F)).toString(16).toUpperCase());
      result += '%' + ((0x80 | ((code >> 6) & 0x3F)).toString(16).toUpperCase());
      result += '%' + ((0x80 | (code & 0x3F)).toString(16).toUpperCase());
    }
  }
  return result;
}

/**
 * Safely decode a percent-encoded string.
 * @param {string} value
 * @returns {string}
 */
function _decodeURIComponent(value) {
  try {
    return decodeURIComponent(value);
  } catch (_e) {
    return value;
  }
}

/**
 * Check if a character is a drive letter (a-z or A-Z).
 * @param {string} ch
 * @returns {boolean}
 */
function _isDriveLetter(ch) {
  return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
}

// Regex to parse a URI: scheme://authority/path?query#fragment
const _uriRegex = /^(([^:/?#]+?):)?(\/\/([^/?#]*))?([^?#]*)(\?([^#]*))?(#(.*))?/;

class Uri {
  /**
   * @param {string} scheme
   * @param {string} authority
   * @param {string} path
   * @param {string} query
   * @param {string} fragment
   * @param {boolean} [_strict] - internal flag, if true skip normalization
   */
  constructor(scheme, authority, path, query, fragment, _strict) {
    this._scheme = scheme || '';
    this._authority = authority || '';
    this._path = path || '';
    this._query = query || '';
    this._fragment = fragment || '';

    if (!_strict) {
      // Normalize: file scheme path should use forward slashes
      if (this._scheme === 'file') {
        this._path = this._path.replace(/\\/g, '/');
      }
    }

    this._formatted = null;
    this._fsPath = null;
  }

  /** @returns {string} */
  get scheme() { return this._scheme; }

  /** @returns {string} */
  get authority() { return this._authority; }

  /** @returns {string} */
  get path() { return this._path; }

  /** @returns {string} */
  get query() { return this._query; }

  /** @returns {string} */
  get fragment() { return this._fragment; }

  /**
   * Returns the OS-native filesystem path.
   * On Windows: backslashes, no leading slash for drive paths.
   * On other OS: same as path.
   * @returns {string}
   */
  get fsPath() {
    if (this._fsPath === null) {
      this._fsPath = _uriToFsPath(this);
    }
    return this._fsPath;
  }

  /**
   * Returns a new Uri with changed components.
   * @param {{ scheme?: string, authority?: string, path?: string, query?: string, fragment?: string }} change
   * @returns {Uri}
   */
  with(change) {
    if (!change) {
      return this;
    }

    const scheme = change.scheme !== undefined ? change.scheme : this._scheme;
    const authority = change.authority !== undefined ? change.authority : this._authority;
    const uriPath = change.path !== undefined ? change.path : this._path;
    const query = change.query !== undefined ? change.query : this._query;
    const fragment = change.fragment !== undefined ? change.fragment : this._fragment;

    if (
      scheme === this._scheme &&
      authority === this._authority &&
      uriPath === this._path &&
      query === this._query &&
      fragment === this._fragment
    ) {
      return this;
    }

    return new Uri(scheme, authority, uriPath, query, fragment);
  }

  /**
   * Serialize to URI string.
   * @param {boolean} [skipEncoding=false] - if true, don't percent-encode
   * @returns {string}
   */
  toString(skipEncoding) {
    if (!skipEncoding) {
      if (this._formatted === null) {
        this._formatted = _formatUri(this, false);
      }
      return this._formatted;
    }
    return _formatUri(this, true);
  }

  /**
   * Returns JSON representation compatible with VS Code.
   * @returns {object}
   */
  toJSON() {
    return {
      $mid: 1,
      scheme: this._scheme,
      authority: this._authority,
      path: this._path,
      query: this._query,
      fragment: this._fragment,
      fsPath: this.fsPath,
      _formatted: this.toString(),
    };
  }

  // --- Static factory methods ---

  /**
   * Creates a file URI from a filesystem path.
   *
   * Windows: C:\Users\file.txt -> path=/c:/Users/file.txt, fsPath=c:\Users\file.txt
   * Unix: /home/user/file.txt -> path=/home/user/file.txt
   * UNC: \\server\share -> file://server/share
   *
   * @param {string} fsPath - filesystem path
   * @returns {Uri}
   */
  static file(fsPath) {
    let authority = '';
    let uriPath = fsPath.replace(/\\/g, '/');

    // Handle UNC paths: //server/share -> authority=server, path=/share
    if (uriPath.length >= 2 && uriPath.charCodeAt(0) === 0x2F && uriPath.charCodeAt(1) === 0x2F) {
      const idx = uriPath.indexOf('/', 2);
      if (idx === -1) {
        authority = uriPath.substring(2);
        uriPath = '/';
      } else {
        authority = uriPath.substring(2, idx);
        uriPath = uriPath.substring(idx);
        if (!uriPath) {
          uriPath = '/';
        }
      }
    }

    // Ensure path starts with /
    if (uriPath.length > 0 && uriPath.charCodeAt(0) !== 0x2F) {
      uriPath = '/' + uriPath;
    }

    // Lowercase drive letter: /C: -> /c:
    if (
      uriPath.length >= 3 &&
      uriPath.charCodeAt(0) === 0x2F &&
      _isDriveLetter(uriPath[1]) &&
      uriPath.charCodeAt(2) === 0x3A
    ) {
      uriPath = '/' + uriPath[1].toLowerCase() + uriPath.substring(2);
    }

    return new Uri('file', authority, uriPath, '', '');
  }

  /**
   * Parse a URI string.
   * @param {string} value - URI string like "file:///c%3A/Users/file.txt"
   * @param {boolean} [strict=false] - if true, validates scheme exists
   * @returns {Uri}
   */
  static parse(value, strict) {
    const match = _uriRegex.exec(value);
    if (!match) {
      return new Uri('', '', '', '', '');
    }

    const scheme = match[2] || '';
    const authority = _decodeURIComponent(match[4] || '');
    const uriPath = _decodeURIComponent(match[5] || '');
    const query = _decodeURIComponent(match[7] || '');
    const fragment = _decodeURIComponent(match[9] || '');

    if (strict && !scheme) {
      throw new Error('Missing scheme in URI: ' + value);
    }

    return new Uri(scheme, authority, uriPath, query, fragment);
  }

  /**
   * Create a Uri from component parts.
   * @param {{ scheme: string, authority?: string, path?: string, query?: string, fragment?: string }} components
   * @returns {Uri}
   */
  static from(components) {
    return new Uri(
      components.scheme,
      components.authority || '',
      components.path || '',
      components.query || '',
      components.fragment || ''
    );
  }

  /**
   * Join path segments onto a base Uri.
   * @param {Uri} base - base URI
   * @param {...string} pathSegments - segments to join
   * @returns {Uri}
   */
  static joinPath(base, ...pathSegments) {
    if (!base || !base.path) {
      throw new Error('Cannot join path on URI without path');
    }

    // Use posix join to keep forward slashes
    const newPath = _posixPath(base.path, ...pathSegments);
    return base.with({ path: newPath });
  }

  /**
   * Revive a Uri from JSON data (deserialization).
   * @param {object|string|undefined} data
   * @returns {Uri|undefined}
   */
  static revive(data) {
    if (!data) {
      return data;
    }
    if (data instanceof Uri) {
      return data;
    }
    if (typeof data === 'string') {
      return Uri.parse(data);
    }
    return new Uri(
      data.scheme || '',
      data.authority || '',
      data.path || '',
      data.query || '',
      data.fragment || '',
      true
    );
  }

  /**
   * Check if a value is a Uri instance or duck-types as one.
   * @param {*} thing
   * @returns {boolean}
   */
  static isUri(thing) {
    if (thing instanceof Uri) {
      return true;
    }
    if (!thing || typeof thing !== 'object') {
      return false;
    }
    return (
      typeof thing.scheme === 'string' &&
      typeof thing.authority === 'string' &&
      typeof thing.path === 'string' &&
      typeof thing.query === 'string' &&
      typeof thing.fragment === 'string' &&
      typeof thing.fsPath === 'string' &&
      typeof thing.with === 'function' &&
      typeof thing.toString === 'function'
    );
  }
}

/**
 * Convert a Uri to an OS-native filesystem path.
 * @param {Uri} uri
 * @returns {string}
 */
function _uriToFsPath(uri) {
  let fsPath;
  const uriPath = uri.path;

  if (uri.authority && uri.scheme === 'file') {
    // UNC path: file://server/share -> //server/share (or \\server\share on Windows)
    fsPath = '//' + uri.authority + uriPath;
  } else if (
    uriPath.length >= 3 &&
    uriPath.charCodeAt(0) === 0x2F &&
    _isDriveLetter(uriPath[1]) &&
    uriPath.charCodeAt(2) === 0x3A
  ) {
    // Windows drive path: /c:/foo -> c:/foo (remove leading slash)
    fsPath = uriPath.substring(1);
  } else {
    fsPath = uriPath;
  }

  if (_isWindows) {
    fsPath = fsPath.replace(/\//g, '\\');
  }

  return fsPath;
}

/**
 * Format a Uri as a string.
 * @param {Uri} uri
 * @param {boolean} skipEncoding
 * @returns {string}
 */
function _formatUri(uri, skipEncoding) {
  const encoder = skipEncoding
    ? function(value) { return value; }
    : function(value, allowSlash, allowColon) { return _encodeURIComponent(value, allowSlash, allowColon); };

  let result = '';

  const scheme = uri.scheme;
  const authority = uri.authority;
  const uriPath = uri.path;
  const query = uri.query;
  const fragment = uri.fragment;

  if (scheme) {
    result += scheme;
    result += ':';
  }

  if (authority || scheme === 'file') {
    result += '//';
  }

  if (authority) {
    let idx = authority.indexOf('@');
    if (idx !== -1) {
      // userinfo@host
      const userinfo = authority.substring(0, idx);
      result += encoder(userinfo, false, false);
      result += '@';
      const hostAndPort = authority.substring(idx + 1);
      idx = hostAndPort.indexOf(':');
      if (idx !== -1) {
        result += encoder(hostAndPort.substring(0, idx), false, false);
        result += ':';
        result += hostAndPort.substring(idx + 1);
      } else {
        result += encoder(hostAndPort, false, false);
      }
    } else {
      idx = authority.indexOf(':');
      if (idx !== -1) {
        result += encoder(authority.substring(0, idx), false, false);
        result += ':';
        result += authority.substring(idx + 1);
      } else {
        result += encoder(authority, false, false);
      }
    }
  }

  if (uriPath) {
    // In path encoding, allow slashes but encode colons
    result += encoder(uriPath, true, false);
  }

  if (query) {
    result += '?';
    result += encoder(query, false, false);
  }

  if (fragment) {
    result += '#';
    result += encoder(fragment, false, false);
  }

  return result;
}

/**
 * Join path segments using posix-style forward slashes.
 * @param {string} basePath
 * @param {...string} segments
 * @returns {string}
 */
function _posixPath(basePath, ...segments) {
  const parts = [basePath];
  for (const seg of segments) {
    parts.push(seg.replace(/\\/g, '/'));
  }

  let joined = path.posix.join(...parts);

  // Preserve leading slash if base had one
  if (basePath.charCodeAt(0) === 0x2F && joined.charCodeAt(0) !== 0x2F) {
    joined = '/' + joined;
  }

  return joined;
}

module.exports = Uri;

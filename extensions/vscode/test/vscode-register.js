// Module registration hook — intercepts require('vscode') and returns the stub.
// Load this via mocha --require BEFORE any test files are loaded.
const Module = require('module');
const path = require('path');

const stubPath = path.resolve(__dirname, 'vscode-stub.js');
const vscodeStub = require(stubPath);

const _resolveFilename = Module._resolveFilename.bind(Module);
Module._resolveFilename = function (request, parent, isMain, options) {
  if (request === 'vscode') return stubPath;
  return _resolveFilename(request, parent, isMain, options);
};

// Pre-populate the cache so subsequent require('vscode') calls get the stub.
require.cache[stubPath] = {
  id: stubPath,
  filename: stubPath,
  loaded: true,
  exports: vscodeStub,
  paths: [],
  children: [],
  parent: null,
};

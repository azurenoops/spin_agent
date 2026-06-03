module.exports = {
  require: ['./test/vscode-register.js'],
  spec: ['./dist/test/suite/**/*.test.js', './dist/test/auth/**/*.test.js'],
  timeout: 10000,
  ui: 'bdd',
  color: true,
};

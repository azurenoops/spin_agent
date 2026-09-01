module.exports = {
  require: ["tsconfig-paths/register", "tsx/cjs"],
  extension: ["ts"],
  spec: "test/**/*.test.ts",
  timeout: 10000,
  recursive: true,
};

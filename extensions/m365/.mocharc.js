module.exports = {
  require: ["tsconfig-paths/register", "ts-node/register"],
  extension: ["ts"],
  spec: "test/**/*.test.ts",
  timeout: 10000,
  recursive: true,
};

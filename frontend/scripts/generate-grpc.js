// gRPC代码生成脚本
// 这个脚本用于从.proto文件生成JavaScript/TypeScript客户端代码

const { execSync } = require('child_process');
const path = require('path');
const fs = require('fs');

const PROTO_DIR = path.join(__dirname, '../../src/CryptoSpot.API/Protos');
const OUTPUT_DIR = path.join(__dirname, '../src/generated');

// 确保输出目录存在
if (!fs.existsSync(OUTPUT_DIR)) {
  fs.mkdirSync(OUTPUT_DIR, { recursive: true });
}

try {
  console.log('开始生成gRPC客户端代码...');
  
  // 生成gRPC-Web客户端代码
  const command = [
    'npx',
    'grpc_tools_node_protoc',
    `--js_out=import_style=commonjs,binary:${OUTPUT_DIR}`,
    `--grpc-web_out=import_style=typescript,mode=grpcweb:${OUTPUT_DIR}`,
    `--proto_path=${PROTO_DIR}`,
    `${PROTO_DIR}/kline.proto`
  ].join(' ');

  console.log('执行命令:', command);
  execSync(command, { stdio: 'inherit' });
  
  console.log('✅ gRPC客户端代码生成成功!');
  console.log(`输出目录: ${OUTPUT_DIR}`);
  
  // 列出生成的文件
  const files = fs.readdirSync(OUTPUT_DIR);
  console.log('生成的文件:');
  files.forEach(file => {
    console.log(`  - ${file}`);
  });
  
} catch (error) {
  console.error('❌ gRPC代码生成失败:', error.message);
  console.log('\n请确保已安装必要的工具:');
  console.log('npm install -g grpc-tools');
  console.log('npm install -g grpc_tools_node_protoc_ts');
  process.exit(1);
}

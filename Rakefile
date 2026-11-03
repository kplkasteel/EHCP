require 'net/http'
require 'rbconfig'
require 'fileutils'

SOLUTION = 'Ehcp.slnx'
# Public Cosmos emulator key (same default as CosmosSettings); override with Cosmos__Key.
EMULATOR_KEY = 'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=='.freeze
BASE_URL = ENV.fetch('Ehcp__BaseUrl', 'http://localhost:5100')
PROCESSING_DIR = 'src/Ehcp.Processing'
# Scratch files the Processing function's Mock Graph/Extraction adapters read fixtures from; the
# Specs project overwrites these with real content before queuing each message (see
# GetEmailSteps.Processing.cs). Just needs to exist at host startup; content is irrelevant until then.
GRAPH_DATA_PATH = File.expand_path("#{PROCESSING_DIR}/.local/graph-fixture.json")
EXTRACTION_DATA_PATH = File.expand_path("#{PROCESSING_DIR}/.local/extraction-fixture.json")

def dotnet(*args)
  sh 'dotnet', *args
end

def configure_environment
  {
    'Cosmos__Endpoint' => 'https://localhost:8081',
    'Cosmos__Key' => EMULATOR_KEY,
    'Cosmos__Database' => 'ehcp-local',
    'Cosmos__Initialize' => 'true',
    'ASPNETCORE_ENVIRONMENT' => 'Development',
    'Ehcp__BaseUrl' => BASE_URL,
    'AzureWebJobsStorage' => 'UseDevelopmentStorage=true',
    'ProcessingQueueName' => 'ehcp-processing',
    'Processing__Graph__Mode' => 'Mock',
    'Processing__Graph__DataPath' => GRAPH_DATA_PATH,
    'Processing__Extraction__Mode' => 'Mock',
    'Processing__Extraction__DataPath' => EXTRACTION_DATA_PATH
  }.each { |name, value| ENV[name] ||= value }
  ENV['EHCP_TEST_COSMOS_KEY'] ||= ENV['Cosmos__Key']
  ENV['EHCP_TEST_COSMOS_ENDPOINT'] ||= ENV['Cosmos__Endpoint']
  ensure_fixture_placeholder(ENV.fetch('Processing__Graph__DataPath'))
  ensure_fixture_placeholder(ENV.fetch('Processing__Extraction__DataPath'))
end

def ensure_fixture_placeholder(path)
  return if File.exist?(path)

  FileUtils.mkdir_p(File.dirname(path))
  File.write(path, '{}')
end

def api_ready?
  Net::HTTP.get_response(URI("#{BASE_URL}/")).is_a?(Net::HTTPSuccess)
rescue SystemCallError, IOError
  false
end

def wait_for_api(pid, timeout: 60)
  deadline = Time.now + timeout
  until api_ready?
    abort 'API process exited before becoming ready.' if Process.waitpid(pid, Process::WNOHANG)
    abort "API not ready after #{timeout}s." if Time.now > deadline
    sleep 1
  end
end

def stop_process(pid)
  if Gem.win_platform?
    system('taskkill', '/T', '/F', '/PID', pid.to_s, out: File::NULL, err: File::NULL)
  else
    Process.kill('TERM', pid)
  end
  Process.wait(pid)
rescue Errno::ESRCH, Errno::ECHILD
  nil
end

# The Processing function host (queue trigger) has no HTTP endpoint to poll for readiness, so just
# confirm it didn't crash immediately (missing func CLI, bad project, etc.) and give it a moment to
# start its worker process; Specs' own "wait for processing status" step already tolerates the rest.
def wait_for_processing(pid, grace: 6)
  deadline = Time.now + grace
  until Time.now > deadline
    abort 'Processing function host exited before becoming ready.' if Process.waitpid(pid, Process::WNOHANG)
    sleep 1
  end
end

desc 'Set environment variables'
task :env do
  configure_environment
end

desc 'Restore packages'
task restore: :env do
  dotnet 'restore', SOLUTION
end

desc 'Build solution'
task build: :restore do
  dotnet 'build', SOLUTION, '--no-restore'
end

desc 'Run unit and API tests'
task unit: :build do
  dotnet 'test', 'tests/Ehcp.Tests', '--no-build'
end

desc 'Run API in the foreground'
task run: :build do
  dotnet 'run', '--no-build', '--project', 'src/Ehcp.Api'
end

desc 'Run the Processing function host in the foreground'
task processing: :build do
  configure_environment
  Dir.chdir(PROCESSING_DIR) { sh 'func', 'start' }
end

desc 'Run Reqnroll specs against an already running API'
task specs: :env do
  dotnet 'test', 'tests/Ehcp.Specs', '--no-build', '--logger', 'trx', '--results-directory', 'tests/Ehcp.Specs/TestResults'
end

desc 'Run only @wip-tagged Reqnroll specs against an already running API'
task wip: :env do
  dotnet 'test', 'tests/Ehcp.Specs', '--no-build', '--filter', 'Category=wip',
    '--logger', 'trx', '--results-directory', 'tests/Ehcp.Specs/TestResults'
end

desc 'Set env vars, build, run unit tests, start the API and Processing host, run Reqnroll specs, stop them'
task go: :unit do
  api_pid = Process.spawn('dotnet', 'run', '--no-build', '--project', 'src/Ehcp.Api')
  processing_pid = Process.spawn('func', 'start', chdir: PROCESSING_DIR)
  begin
    wait_for_api(api_pid)
    wait_for_processing(processing_pid)
    Rake::Task[:specs].invoke
  ensure
    stop_process(processing_pid)
    stop_process(api_pid)
  end
end

task default: :go

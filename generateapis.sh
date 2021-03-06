#!/bin/bash

set -e

# TODO: Use some appropriate way of determining OS. Ideally, shouldn't need dotnet installed.
#[[ $(dotnet --info | grep "OS Platform" | grep -c Windows) -ne 0 ]] && OS=windows || OS=linux
OS=linux
[[ ${OS} = "windows" ]] && EXE_SUFFIX=.exe || EXE_SUFFIX=

GRPC_VERSION=1.0.1-pre1
PROTOBUF_VERSION=3.1.0
PROTOC=packages/Grpc.Tools.$GRPC_VERSION/tools/${OS}_x64/protoc${EXE_SUFFIX}
GRPC_PLUGIN=packages/Grpc.Tools.$GRPC_VERSION/tools/${OS}_x64/grpc_csharp_plugin${EXE_SUFFIX}
CORE_PROTOS_ROOT=packages/Google.Protobuf.Tools.$PROTOBUF_VERSION/tools
OUTDIR=tmp

# Nuget isn't working nicely for me on Linux...
nuget_install() {
  outdir=packages/$1.$2
  if [ ! -d "$outdir" ]
  then
    echo Fetching $1 version $2
    mkdir -p $outdir
    wget --quiet -Otmp.zip https://www.nuget.org/api/v2/package/$1/$2    
    unzip -q -d $outdir tmp.zip
    if [ -d "packages/$1.$2/tools" ]
    then 
      chmod +x `find packages/$1.$2/tools -type f`
    fi

    rm tmp.zip
  fi
}

install_dependencies() {
  # Make sure we have all the tools we need.
  # Prerequisite: Java already installed so that gradlew will work
  nuget_install Google.Protobuf.Tools $PROTOBUF_VERSION
  nuget_install Google.Protobuf $PROTOBUF_VERSION
  nuget_install Grpc.Tools $GRPC_VERSION
  
  if [ -d "toolkit" ]
  then
    pushd toolkit > /dev/null
    git pull
    git submodule update
    popd > /dev/null
  else
    git clone --recursive  https://github.com/googleapis/toolkit
  fi
          
  if [ -d "googleapis" ]
  then
    pushd googleapis > /dev/null
    git pull
    popd > /dev/null
  else
    git clone --recursive  https://github.com/googleapis/googleapis
  fi
}

generate_api() {
  API_TMP_DIR=$OUTDIR/$1
  API_OUT_DIR=apis/$1
  API_SRC_DIR=googleapis/$2
  API_YAML=$API_SRC_DIR/../$3
  [[ "$4" != "" ]] && EXTRA_PROTOS=googleapis/$4/*.proto || EXTRA_PROTOS=
  
  echo Generating $1
  mkdir $API_TMP_DIR
  
  # Generate the C# protos/gRPC
  $PROTOC \
    --csharp_out=$API_TMP_DIR \
    --grpc_out=$API_TMP_DIR \
    -I googleapis \
    -I $CORE_PROTOS_ROOT \
    --plugin=protoc-gen-grpc=$GRPC_PLUGIN \
    $API_SRC_DIR/*.proto \
    $EXTRA_PROTOS
  
  # There should be only one gapic yaml file...
  for i in $API_SRC_DIR/*_gapic.yaml
  do
    cp $i $API_TMP_DIR/gapic.yaml
  done
  cat >> $API_TMP_DIR/gapic.yaml << EOF
language: csharp
generator:
  factory: com.google.api.codegen.gapic.MainGapicProviderFactory
  id: csharp
EOF
  
  pushd toolkit > /dev/null
  ./gradlew -q runVGen -Pclargs=--descriptor_set=../$OUTDIR/protos.desc,--service_yaml=../$API_YAML,--gapic_yaml=../$API_TMP_DIR/gapic.yaml,--output=../$API_TMP_DIR
  popd > /dev/null

  for f in `find $API_TMP_DIR -type f -name '*.cs'`
  do
    ns=`grep '^namespace' $f | cut -d ' ' -f 2`
    mkdir -p $API_OUT_DIR/$ns
    mv $f $API_OUT_DIR/$ns
  done
}

# Entry point

install_dependencies

OUTDIR=tmp
rm -rf $OUTDIR
mkdir $OUTDIR
# Generate a single descriptor file for all protos we may care about.
# We can then reuse that for all APIs.
$PROTOC \
  -I googleapis \
  -I $CORE_PROTOS_ROOT \
  --include_source_info \
  -o $OUTDIR/protos.desc \
  $CORE_PROTOS_ROOT/google/protobuf/*.proto \
  `find googleapis -name '*.proto'`

# Now the per-API codegen  
generate_api Google.Cloud.Vision.V1 google/cloud/vision/v1 vision.yaml
generate_api Google.Cloud.Language.V1Beta1 google/cloud/language/v1beta1 language.yaml
generate_api Google.Cloud.Speech.V1Beta1 google/cloud/speech/v1beta1 cloud_speech.yaml
generate_api Google.Logging.V2 google/logging/v2 logging.yaml google/logging/type
generate_api Google.Devtools.Cloudtrace.V1 google/devtools/cloudtrace/v1 trace.yaml
generate_api Google.Devtools.Clouderrorreporting.V1Beta1 google/devtools/clouderrorreporting/v1beta1 errorreporting.yaml
generate_api Google.Longrunning google/longrunning longrunning/longrunning.yaml
generate_api Google.Pubsub.V1 google/pubsub/v1 pubsub.yaml
generate_api Google.Datastore.V1 google/datastore/v1 datastore.yaml
generate_api Google.Monitoring.V3 google/monitoring/v3 monitoring.yaml

# TODO: Generate just the grpc/protos for IAM.

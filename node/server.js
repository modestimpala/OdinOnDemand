// this script just handles incoming http requsts at localhost:$port/yt/ url and returns the direct link using youtube-dl-exec 
// the url must be followed by an /$auth token see authString below

const express = require('express');
const youtubedl = require('youtube-dl-exec');

const app = express();
const port = process.env.PORT || 3000; 

// ** CHANGE ME **
// https://generate.plus/en/base64 make it like ~6-8 characters and select "URL safe" (equal signs are OK, throw on a few at the end for fun)
const authString = "aFfiyQLctKg="; // GENERATE OWN AUTH CODE HERE 

app.get('/yt/:name/:auth', function(req, res) {
  res.send(req.params.name);
});

const errorLogger = (error, request, response, next) => {
    console.log( `error ${error.message}`) 
    next(error) 
  }
const errorResponder = (error, request, response, next) => {
  response.header("Content-Type", 'application/json')
    
  const status = error.status || 400
  response.status(status).send(error.message)
}
const invalidPathHandler = (request, response, next) => {
  response.status(404)
  response.send('invalid path')
}

app.param('name', async function(req, res, next, name) {
	const sanitizedAuth = req.params.auth.replace(/[,+'"*<>{}]/g, '');
	if(sanitizedAuth == authString) {
		try {
			console.log("Responing to query for url: " + name + " from: " + req.ip);
			const modified = name.replace(/[,+'"*<>{}]/g, ''); //sanitize 
			const output = await youtubedl(modified, {
				noWarnings: true,
				noCallHome: true,
				noCheckCertificate: true,
				preferFreeFormats: true,
				youtubeSkipDashManifest: true,
				getUrl: true,
				//format: "acodec:vorbis",
				formatSort: "+acodec:mp4a",
				referer: 'https://example.com'
				});
			const jsonContent = JSON.stringify(output);
			req.params.name = jsonContent;
			next();
		} catch(err) {
			console.error(err);
			req.params.name = "YT-DLP ERROR";
			res.status(400);
			next(err);
		}
	} else {
		req.params.name = "AUTH DENIED - TOKEN: " + sanitizedAuth;
		console.log("Denied access to " + req.ip + "for url " + name + "with token " + sanitizedAuth);
		next();
	}
    
  });
 

app.use(errorLogger)
app.use(errorResponder)
app.use(invalidPathHandler)

app.listen(port);
console.log('Server started at http://localhost:' + port);
